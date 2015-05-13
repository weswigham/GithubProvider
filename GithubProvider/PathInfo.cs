using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace GithubProvider
{
    internal enum PathType
    {
        Invalid,
        Root,
        User,
        Org,
        Repo,
        Folder,
        File
    }

    internal abstract class PathInfo
    {
        public PathType Type { get; set; } = PathType.Invalid;

        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        public virtual Task<IEnumerable<PathInfo>> Children()
        {
            return Task.FromResult<IEnumerable<PathInfo>>(new List<PathInfo>());
        }

        public virtual Task<bool> Exists()
        {
            return Task.FromResult(false);
        }

        public virtual object AsObject()
        {
            if (Type == PathType.File)
            {
                return new System.IO.FileInfo(VirtualPath); //These types seem to print right, it's odd.
            }
            else
            {
                if (string.IsNullOrEmpty(VirtualPath))
                {
                    return new System.IO.DirectoryInfo(Path.DirectorySeparatorChar.ToString());
                }
                return new System.IO.DirectoryInfo(VirtualPath);
            }
        }

        public virtual string VirtualPath { get; protected set; }

        public static MemoryCache PathInfoCache = new MemoryCache("GithubPSProvider");

        private static int parsePathIntoParts(string path, out string[] parts)
        {
            var sections = string.IsNullOrWhiteSpace(path) ? new string[] { } : path.Split(Path.DirectorySeparatorChar);
            foreach (var elem in sections.Take(sections.Length - 1))
            {
                if (string.IsNullOrWhiteSpace(elem))
                {
                    parts = new string[] { };
                    return -1;
                }
            }
            if (sections.Length == 0)
            {
                parts = sections;
                return 0;
            }
            if (string.IsNullOrWhiteSpace(sections[sections.Length - 1]))
            {
                //drop tailing slash
                sections = sections.Take(sections.Length - 1).ToArray();
            }
            parts = sections;
            return sections.Length;
        }

        public static UserInfo UncheckedUserFromPath(string path)
        {
            string[] sections;
            var sectionCount = parsePathIntoParts(path, out sections);
            if (sectionCount != 1) return null;
            return new UserInfo(sections[0]);
        }

        public static OrgInfo UncheckedOrgFromPath(string path)
        {
            string[] sections;
            var sectionCount = parsePathIntoParts(path, out sections);
            if (sectionCount != 1) return null;
            return new OrgInfo(sections[0]);
        }

        public static RepoInfo UncheckedRepoFromPath(string path)
        {
            string[] sections;
            var sectionCount = parsePathIntoParts(path, out sections);
            if (sectionCount != 2) return null;
            return new RepoInfo(sections[0], sections[1]);
        }

        public static FileInfo UncheckedFileFromPath(string path)
        {
            string[] sections;
            var sectionCount = parsePathIntoParts(path, out sections);
            if (sectionCount <= 2) return null;
            var filepath = Path.Combine(sections.Skip(2).Take(sections.Length - 2).ToArray());
            return new FileInfo(sections[0], sections[1], filepath, null);
        }

        public static FolderInfo UncheckedFolderFromPath(string path)
        {
            string[] sections;
            var sectionCount = parsePathIntoParts(path, out sections);
            if (sectionCount <= 2) return null;
            var filepath = Path.Combine(sections.Skip(2).Take(sections.Length - 2).ToArray());
            return new FolderInfo(sections[0], sections[1], filepath, null);
        }

        public static async Task<PathInfo> FromFSPath(string path)
        {
            if (PathInfoCache.Contains(path))
            {
                return PathInfoCache.Get(path) as PathInfo;
            }
            string[] sections;
            var sectionCount = parsePathIntoParts(path, out sections);
            switch (sectionCount)
            {
                case -1:
                    return null;
                case 0:
                    {
                        if (PathInfoCache.Contains(""))
                        {
                            return PathInfoCache.Get("") as PathInfo;
                        }
                        else
                        {
                            PathInfoCache[""] = new RootInfo();
                            return PathInfoCache.Get("") as PathInfo;
                        }
                    }
                case 1:
                    {
                        var org = new OrgInfo(sections[0]);
                        if (await org.Exists())
                        {
                            return org;
                        }
                        var user = new UserInfo(sections[0]);
                        if (await user.Exists())
                        {
                            return user;
                        }
                        return null;
                    }
                case 2:
                    {
                        var repo = new RepoInfo(sections[0], sections[1]);
                        return await repo.Exists() ? repo : null;
                    }
                default:
                    {
                        var filepath = Path.Combine(sections.Skip(2).Take(sections.Length - 2).ToArray());
                        var folder = new FolderInfo(sections[0], sections[1], filepath, null);
                        if (await folder.Exists())
                        {
                            return folder;
                        }
                        var file = new FileInfo(sections[0], sections[1], filepath, null);
                        if (await file.Exists())
                        {
                            return file;
                        }
                        return null;
                    }
            }
        }
    }

    internal class RootInfo : PathInfo
    {
        public RootInfo()
        {
            Name = "Root";
            Type = PathType.Root;
        }

        public override string VirtualPath
        {
            get
            {
                return "";
            }
        }

        public override Task<bool> Exists()
        {
            return Task.FromResult(true);
        }

        public override async Task<IEnumerable<PathInfo>> Children()
        {
            var user = await GithubProvider.Client.User.Current();
            var username = user.Login;
            var orgs = await GithubProvider.Client.Organization.GetAllForCurrent();
            var orgInfos = orgs.Select((org) => new OrgInfo(org.Login));
            var items = orgInfos.Concat(new List<PathInfo>() { new UserInfo(username) });
            foreach (var item in items)
            {
                PathInfoCache[item.VirtualPath] = item;
            }
            return items;
        }
    }

    internal abstract class RepoCollectionInfo : PathInfo
    {
        public RepoCollectionInfo(string name)
        {
            Name = name;
        }

        public override string VirtualPath { get { return Name; } }
    }

    internal class UserInfo : RepoCollectionInfo
    {
        public UserInfo(string name) : base(name)
        {
            Type = PathType.User;
        }

        public override async Task<IEnumerable<PathInfo>> Children()
        {
            var repos = await GithubProvider.Client.Repository.GetAllForUser(Name);
            var items = repos.Select((repo) => new RepoInfo(Name, repo.Name));
            foreach (var item in items)
            {
                PathInfoCache[item.VirtualPath] = item;
            }
            return items;
        }

        public override async Task<bool> Exists()
        {
            try
            {
                var user = await GithubProvider.Client.User.Get(Name);
                if (user != null)
                {
                    PathInfoCache[Name] = this;
                    return true;
                }
            }
            catch (Octokit.NotFoundException)
            {
            }
            return false;
        }
    }

    internal class OrgInfo : RepoCollectionInfo
    {
        public OrgInfo(string name) : base(name)
        {
            Type = PathType.Org;
        }

        public override async Task<IEnumerable<PathInfo>> Children()
        {
            var repos = await GithubProvider.Client.Repository.GetAllForOrg(Name);
            var items = repos.Select((repo) => new RepoInfo(Name, repo.Name));
            foreach (var item in items)
            {
                PathInfoCache[item.VirtualPath] = item;
            }
            return items;
        }

        public override async Task<bool> Exists()
        {
            try
            {
                var org = await GithubProvider.Client.Organization.Get(Name);
                if (org != null)
                {
                    PathInfoCache[Name] = this;
                    return true;
                }
            }
            catch (Octokit.NotFoundException)
            {
            }
            return false;
        }
    }

    internal class RepoInfo : PathInfo
    {
        public RepoInfo(string org, string name)
        {
            Org = org;
            Name = name;
            Type = PathType.Repo;
        }

        [ValidateNotNullOrEmpty]
        public string Org { get; set; }

        public override string VirtualPath
        {
            get
            {
                return Path.Combine(Org, Name);
            }
        }

        public override async Task<IEnumerable<PathInfo>> Children()
        {
            var repo = await GithubProvider.Client.Repository.Get(Org, Name);
            Branch defaultBranch;
            try {
                defaultBranch = await GithubProvider.Client.Repository.GetBranch(Org, Name, repo.DefaultBranch);
            } catch (Octokit.NotFoundException)
            { //Empty uninitialized repo
                return new List<PathInfo>();
            }

            var tree = await GithubProvider.Client.GitDatabase.Tree.Get(Org, Name, defaultBranch.Commit.Sha);
            if (tree.Truncated)
            {
                throw new Exception("Repo too big.");
            }
            var items = tree.Tree.Select<TreeItem, PathInfo>((item) =>
            {
                switch (item.Type)
                {
                    case TreeType.Blob:
                        return new FileInfo(Org, Name, item.Path, item.Sha);
                    case TreeType.Tree:
                        return new FolderInfo(Org, Name, item.Path, item.Sha);
                    default:
                        return null;
                }
            }).Where((o) => o != null);
            foreach (var item in items)
            {
                PathInfoCache[item.VirtualPath] = item;
            }
            return items;
        }

        public override async Task<bool> Exists()
        {
            try
            {
                var repo = await GithubProvider.Client.Repository.Get(Org, Name);
                if (repo != null)
                {
                    PathInfoCache[VirtualPath] = this;
                    return true;
                }
            } catch (Octokit.NotFoundException)
            {
            }
            return false;
        }
    }

    internal abstract class FilesystemInfo : PathInfo
    {
        public FilesystemInfo(string org, string repo, string path, string sha)
        {
            Org = org;
            Repo = repo;
            FilePath = path;
            Sha = sha;
            Name = Path.GetFileName(path);
        }
        
        public string FilePath { get; private set; }
        
        public string Org { get; private set; }
        
        public string Repo { get; private set; }
        
        public string Sha { get; private set; }

        public override string VirtualPath
        {
            get
            {
                return Path.Combine(Org, Repo, FilePath);
            }
        }

        public override async Task<bool> Exists()
        {
            try
            {
                if (Sha != null)
                {
                    if (Type == PathType.Folder)
                    {
                        return await GithubProvider.Client.GitDatabase.Tree.Get(Org, Repo, Sha) != null;
                    }
                    else
                    {
                        return await GithubProvider.Client.GitDatabase.Blob.Get(Org, Repo, Sha) != null;
                    }
                }

                //Otherwise lookup the sha
                var repo = await GithubProvider.Client.Repository.Get(Org, Repo);
                var defaultBranch = await GithubProvider.Client.Repository.GetBranch(Org, Repo, repo.DefaultBranch);
                var filepath = FilePath;
                var files = await GithubProvider.Client.GitDatabase.Tree.GetRecursive(Org, Repo, defaultBranch.Commit.Sha);
                if (files.Tree.Count > 0)
                {
                    foreach (var file in files.Tree)
                    {
                        if (file.Path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar) == filepath)
                        {
                            if ((file.Type == TreeType.Blob && Type == PathType.File)
                                ||
                                (file.Type == TreeType.Tree && Type == PathType.Folder))
                            {
                                Sha = file.Sha;
                                PathInfoCache[VirtualPath] = this;
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Octokit.NotFoundException)
            {
            }
            return false;
        }
    }

    internal class FolderInfo : FilesystemInfo
    {
        public FolderInfo(string org, string repo, string path, string sha) : base(org, repo, path, sha)
        {
            Type = PathType.Folder;
        }

        public override async Task<IEnumerable<PathInfo>> Children()
        {
            var tree = await GithubProvider.Client.GitDatabase.Tree.Get(Org, Repo, Sha);
            if (tree.Truncated)
            {
                throw new Exception("Repo too big.");
            }
            var items = tree.Tree.Select<TreeItem, PathInfo>((item) =>
            {
                switch (item.Type)
                {
                    case TreeType.Blob:
                        return new FileInfo(Org, Repo, item.Path, item.Sha);
                    case TreeType.Tree:
                        return new FolderInfo(Org, Repo, item.Path, item.Sha);
                    default:
                        return null;
                }
            }).Where((o) => o != null);
            foreach (var item in items)
            {
                PathInfoCache[item.VirtualPath] = item;
            }
            return items;
        }
    }

    internal class FileInfo : FilesystemInfo
    {
        public FileInfo(string org, string repo, string path, string sha) : base(org, repo, path, sha)
        {
            Type = PathType.File;
        }
    }
}

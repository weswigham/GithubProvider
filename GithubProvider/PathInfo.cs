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

        public static async Task<PathInfo> FromFSPath(string path)
        {
            if (PathInfoCache.Contains(path))
            {
                return PathInfoCache.Get(path) as PathInfo;
            }
            var sections = string.IsNullOrWhiteSpace(path) ? new string[] { } : path.Split(Path.DirectorySeparatorChar);
            foreach (var elem in sections.Take(sections.Length - 1))
            {
                if (string.IsNullOrWhiteSpace(elem))
                {
                    return null;
                }
            }
            if (sections.Length == 0)
            {
                if (PathInfoCache.Contains(""))
                {
                    return PathInfoCache.Get("") as PathInfo;
                } else
                {
                    PathInfoCache[""] = new RootInfo();
                    return PathInfoCache.Get("") as PathInfo;
                }
            }
            if (string.IsNullOrWhiteSpace(sections[sections.Length - 1]))
            {
                //drop tailing slash
                sections = sections.Take(sections.Length - 1).ToArray();
            }
            switch (sections.Length)
            {
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
                        try
                        {
                            var org = await GithubProvider.Client.Organization.Get(sections[0]);
                            if (org != null)
                            {
                                PathInfoCache[path] = new OrgInfo(sections[0]);
                            }
                        }
                        catch (Octokit.NotFoundException)
                        {
                        }
                        try
                        {
                            var user = await GithubProvider.Client.User.Get(sections[0]);
                            if (user != null)
                            {
                                PathInfoCache[path] = new UserInfo(user.Login);
                            }
                        }
                        catch (Octokit.NotFoundException)
                        {
                        }
                        if (PathInfoCache.Contains(path))
                        {
                            return PathInfoCache.Get(path) as PathInfo;
                        }
                        else
                        {
                            return null;
                        }
                    }
                case 2:
                    {
                        try
                        {
                            var repo = await GithubProvider.Client.Repository.Get(sections[0], sections[1]);
                            if (repo == null)
                            {
                                return null;
                            }
                            PathInfoCache[path] = new RepoInfo(sections[0], sections[1]);
                        }
                        catch (Octokit.NotFoundException)
                        {
                        }
                        if (PathInfoCache.Contains(path))
                        {
                            return PathInfoCache.Get(path) as PathInfo;
                        }
                        else
                        {
                            return null;
                        }
                    }
                default:
                    {
                        try
                        {
                            var repo = await GithubProvider.Client.Repository.Get(sections[0], sections[1]);
                            var defaultBranch = await GithubProvider.Client.Repository.GetBranch(sections[0], sections[1], repo.DefaultBranch);
                            var filepath = Path.Combine(sections.Skip(2).Take(sections.Length - 2).ToArray());
                            var files = await GithubProvider.Client.GitDatabase.Tree.GetRecursive(sections[0], sections[1], defaultBranch.Commit.Sha);
                            if (files.Tree.Count > 0)
                            {
                                foreach (var file in files.Tree)
                                {
                                    if (file.Path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar) == filepath)
                                    {
                                        if (file.Type == TreeType.Blob)
                                        {
                                            PathInfoCache[path] = new FileInfo(sections[0], sections[1], filepath, file.Sha);
                                        }
                                        else
                                        {
                                            PathInfoCache[path] = new FolderInfo(sections[0], sections[1], filepath, file.Sha);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Octokit.NotFoundException)
                        {
                        }
                        if (PathInfoCache.Contains(path))
                        {
                            return PathInfoCache.Get(path) as PathInfo;
                        }
                        else
                        {
                            return null;
                        }
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
            var defaultBranch = await GithubProvider.Client.Repository.GetBranch(Org, Name, repo.DefaultBranch);

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

        [ValidateNotNullOrEmpty]
        public string FilePath { get; private set; }

        [ValidateNotNullOrEmpty]
        public string Org { get; private set; }

        [ValidateNotNullOrEmpty]
        public string Repo { get; private set; }

        [ValidateNotNullOrEmpty]
        public string Sha { get; private set; }

        public override string VirtualPath
        {
            get
            {
                return Path.Combine(Org, Repo, FilePath);
            }
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

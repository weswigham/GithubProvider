using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Management.Automation.Provider;
using Octokit;
using System.IO;
using System.Management.Automation;
using System.Collections.ObjectModel;
using Octokit.Internal;
using Octokit.Caching;
using System.Runtime.Serialization;
using System.Diagnostics.Contracts;
using System.Collections.Concurrent;

namespace GithubProvider
{
    public static class TaskPlus
    {
        public static T Resolve<T>(this Task<T> task)
        {
            return task.GetAwaiter().GetResult();
        }
    }

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

        public static ConcurrentDictionary<string, PathInfo> PathInfoCache = new ConcurrentDictionary<string, PathInfo>();

        public static async Task<PathInfo> FromFSPath(string path)
        {
            PathInfo cached;
            if (PathInfoCache.TryGetValue(path, out cached))
            {
                return cached;
            }
            var sections = string.IsNullOrWhiteSpace(path) ? new string[] { } : path.Split(Path.DirectorySeparatorChar);
            switch (sections.Length)
            {
                case 0: //this probably isn't possible
                    {
                        PathInfoCache[""] = new RootInfo();
                        return PathInfoCache[""];
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
                        PathInfoCache.TryGetValue(path, out cached);
                        return cached;
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
                        PathInfoCache.TryGetValue(path, out cached);
                        return cached;
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
                                    if (file.Path == filepath)
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
                        PathInfoCache.TryGetValue(path, out cached);
                        return cached;
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



    [CmdletProvider("Github", ProviderCapabilities.None)]
    public class GithubProvider : NavigationCmdletProvider, IContentCmdletProvider
    {
        private static GitHubClient _client;
        private static GitHubClient makeClient()
        {
            var connection = new Connection(
                new ProductHeaderValue("GithubPSProvider", "0.0.1"),
                GitHubClient.GitHubApiUrl,
                new InMemoryCredentialStore(new Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))),
                new CachingHttpClient(new HttpClientAdapter(), new NaiveInMemoryCache()),
                new SimpleJsonSerializer());
            var client = new GitHubClient(connection);
            return client;
        }
        public static GitHubClient Client
        {
            get
            {
                _client = _client ?? (_client = makeClient());
                return _client;
            }
        }

        public void ClearContent(string path)
        {
            throw new NotImplementedException();
        }

        public object ClearContentDynamicParameters(string path)
        {
            throw new NotImplementedException();
        }

        public IContentReader GetContentReader(string path)
        {
            var info = PathInfo.FromFSPath(path).Resolve();
            if (info.Type == PathType.File)
            {
                var fileInfo = info as FileInfo;
                var content = Client.Repository.Content.GetAllContents(fileInfo.Org, fileInfo.Repo, fileInfo.FilePath).Resolve().FirstOrDefault();
                return new HttpFileReader(content.DownloadUrl);
            }
            return null;
        }

        public object GetContentReaderDynamicParameters(string path)
        {
            return null;
        }

        public IContentWriter GetContentWriter(string path)
        {
            throw new NotImplementedException();
        }

        public object GetContentWriterDynamicParameters(string path)
        {
            throw new NotImplementedException();
        }

        protected override bool IsValidPath(string path)
        {
            return true; //I unno
        }

        protected override void GetItem(string path)
        {
            var info = PathInfo.FromFSPath(path).Resolve();
            WriteItemObject(info.AsObject(), info.VirtualPath, info.Type != PathType.File);
        }

        protected override bool IsItemContainer(string path)
        {
            var info = PathInfo.FromFSPath(path).Resolve();
            return info.Type != PathType.File;
        }

        protected override bool HasChildItems(string path)
        {
            var info = PathInfo.FromFSPath(path).Resolve();
            return info.Children().Resolve().Count() > 0;
        }

        protected override void GetChildItems(string path, bool recurse)
        {
            var info = PathInfo.FromFSPath(path).Resolve();
            if (info == null)
            {
                return;
            }

            var children = info.Children().Resolve();
            foreach (var child in children)
            {
                if (recurse)
                {
                    GetChildItems(Path.Combine(path, child.Name), recurse);
                }
                WriteItemObject(child.AsObject(), child.VirtualPath, child.Type != PathType.File);
            }
        }

        protected override string GetChildName(string path)
        {
            var info = PathInfo.FromFSPath(path).Resolve();
            if (info == null)
            {
                return null;
            }
            return info.Name;
        }

        protected override string GetParentPath(string path, string root)
        {
            // If root is specified then the path has to contain
            // the root. If not nothing should be returned
            if (!String.IsNullOrEmpty(root))
            {
                if (!path.Contains(root))
                {
                    return null;
                }
            }

            var index = path.LastIndexOf(Path.DirectorySeparatorChar) >= 0 ? path.LastIndexOf(Path.DirectorySeparatorChar) : 0;
            return path.Substring(0, index);
        }

        protected override string MakePath(string parent, string child)
        {
            return Path.Combine(parent, child);
        }

        protected override void GetChildNames(string path, ReturnContainers returnContainers)
        {
            var info = PathInfo.FromFSPath(path).Resolve();
            if (info == null)
            {
                return;
            }

            var children = info.Children().Resolve();
            foreach (var child in children)
            {
                WriteItemObject(child.Name, child.VirtualPath, child.Type != PathType.File);
            }
        }

        protected override PSDriveInfo NewDrive(PSDriveInfo drive)
        {
            // Check if the drive object is null.
            if (drive == null)
            {
                WriteError(new ErrorRecord(
                           new ArgumentNullException("drive"),
                           "Null Drive",
                           ErrorCategory.InvalidArgument,
                           null));

                return null;
            }

            var info = new GithubDriveInfo(drive);
            return info;
        }

        protected override PSDriveInfo RemoveDrive(PSDriveInfo drive)
        {
            var upcast = drive as GithubDriveInfo;
            if (upcast == null)
            {
                return null;
            }
            return upcast;
        }

        protected override Collection<PSDriveInfo> InitializeDefaultDrives()
        {
            var drives = new Collection<PSDriveInfo>();
            drives.Add(NewDrive(new PSDriveInfo(
                "GH",
                ProviderInfo,
                "",
                "drive to access github",
                null
            )));
            return drives;
        }

        protected override bool ItemExists(string path)
        {
            var info = PathInfo.FromFSPath(path).Resolve();
            return info != null;
        }
    }
}

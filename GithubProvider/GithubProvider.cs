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
        Root,
        User,
        Org,
        Repo,
        Folder,
        File
    }

    internal abstract class PathInfo
    {
        public PathType Type {get; set;}

        public string Name { get; set; }

        public virtual async Task<IEnumerable<PathInfo>> Children()
        {
            return new List<PathInfo>();
        }

        public virtual object AsObject()
        {
            if (Type == PathType.File)
            {
                return new System.IO.FileInfo(VirtualPath); //These types seem to print right, it's odd.
            }
            else
            {
                return new System.IO.DirectoryInfo(VirtualPath);
            }
        }

        public virtual string VirtualPath { get; protected set; }

        public static async Task<PathInfo> FromFSPath(string path)
        {
            var sections = String.IsNullOrWhiteSpace(path) ? new string[] { } : path.Split(Path.DirectorySeparatorChar);
            switch (sections.Length)
            {
                case 0: //this probably isn't possible
                    {
                        return new RootInfo();
                    }
                case 1:
                    {
                        return (await new RootInfo().Children()).Where((child) => (child as RepoCollectionInfo).Name == sections[0]).FirstOrDefault();
                    }
                case 2:
                    {
                        return (await (await FromFSPath(sections[0])).Children()).Where((child) => (child as RepoInfo).Name == sections[1]).FirstOrDefault();
                    }
                default:
                    {
                        return (await (await FromFSPath(
                                    String.Join(
                                        Path.DirectorySeparatorChar.ToString(), 
                                        sections.Take(2)
                                    )
                                )).Children())
                            .Where(
                                (child) => 
                                    (child as FilesystemInfo).FilePath 
                                    == 
                                    String.Join(
                                        Path.DirectorySeparatorChar.ToString(), 
                                        sections.Skip(2).Take(sections.Length - 2)
                                    )
                            )
                            .FirstOrDefault();
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
            return orgInfos.Concat(new List<PathInfo>() { new UserInfo(username) });
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
            var repos = await GithubProvider.Client.Repository.GetAllForCurrent();
            return repos.Select((repo) => new RepoInfo(Name, repo.Name));
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
            return repos.Select((repo) => new RepoInfo(Name, repo.Name));
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

            var tree = await GithubProvider.Client.GitDatabase.Tree.GetRecursive(Org, Name, defaultBranch.Commit.Sha);
            if (tree.Truncated)
            {
                throw new Exception("Repo too big.");
            }
            return tree.Tree.Select<TreeItem, PathInfo>((item) =>
            {
                switch (item.Type)
                {
                    case TreeType.Blob:
                        return new FileInfo(Org, Name, item.Path);
                    case TreeType.Tree:
                        return new FolderInfo(Org, Name, item.Path);
                    default:
                        return null;
                }
            }).Where((o) => o != null && o.VirtualPath == Path.Combine(Org, Name, o.Name));
        }
    }

    internal abstract class FilesystemInfo : PathInfo
    {
        public FilesystemInfo(string org, string repo, string path)
        {
            Org = org;
            Repo = repo;
            FilePath = path;
            Name = Path.GetFileName(path);
        }

        public string FilePath { get; set; }

        public string Org { get; set; }

        public string Repo { get; set; }

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
        public FolderInfo(string org, string repo, string path) : base(org, repo, path)
        {
            Type = PathType.Folder;
        }

        public override async Task<IEnumerable<PathInfo>> Children()
        {
            var repo = await GithubProvider.Client.Repository.Get(Org, Repo);
            var defaultBranch = await GithubProvider.Client.Repository.GetBranch(Org, Repo, repo.DefaultBranch);

            var tree = await GithubProvider.Client.GitDatabase.Tree.GetRecursive(Org, Repo, defaultBranch.Commit.Sha);
            if (tree.Truncated)
            {
                throw new Exception("Repo too big.");
            }
            return tree.Tree.Select<TreeItem, PathInfo>((item) =>
            {
                switch (item.Type)
                {
                    case TreeType.Blob:
                        return new FileInfo(Org, Repo, item.Path);
                    case TreeType.Tree:
                        return new FolderInfo(Org, Repo, item.Path);
                    default:
                        return null;
                }
            }).Where((o) => o != null && o.VirtualPath == Path.Combine(VirtualPath, o.Name));
        }
    }

    internal class FileInfo : FilesystemInfo
    {
        public FileInfo(string org, string repo, string path) : base(org, repo, path)
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
            throw new NotImplementedException();
        }

        public object GetContentReaderDynamicParameters(string path)
        {
            throw new NotImplementedException();
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
            return ItemExists(path);
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

            foreach (var child in info.Children().Resolve())
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

            foreach (var child in info.Children().Resolve())
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

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Management.Automation.Provider;
using Octokit;
using System.IO;
using System.Management.Automation;
using System.Collections.ObjectModel;
using Octokit.Internal;
using Octokit.Caching;
using System.Collections.Generic;

namespace GithubProvider
{
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

        protected Dictionary<string, Func<string, object, Task<object>>> itemCreationHandlers
            = new Dictionary<string, Func<string, object, Task<object>>>() {
                { "Directory", async (path, input) =>
                {
                    var folder = PathInfo.UncheckedFolderFromPath(path);
                    if (folder == null)
                    {
                        var repoInfo = PathInfo.UncheckedRepoFromPath(path);
                        if (repoInfo == null)
                        {
                            throw new NotSupportedException("The Github Provider does not support making directories in this location.");
                        }
                        if (await repoInfo.Exists())
                        {
                            throw new Exception("The given repository already exists."); //TODO: Custom exceptions
                        }
                        var parentInfo = await PathInfo.FromFSPath(GithubProvider.StaticGetParentPath(path, null));
                        if (parentInfo != null)
                        {
                            var repo = new NewRepository(repoInfo.Name) { AutoInit = true };
                            switch(parentInfo.Type)
                            {
                                case (PathType.User):
                                    {
                                        await GithubProvider.Client.Repository.Create(repo);
                                        return repoInfo;
                                    }
                                case (PathType.Org):
                                    {
                                        await GithubProvider.Client.Repository.Create(repoInfo.Org, repo);
                                        return repoInfo;
                                    }
                                default:
                                    throw new Exception("Given path is neither a repository nor a folder and cannot be created."); //TODO
                            }
                        }
                        
                    }
                    if (await folder.Exists())
                    {
                        throw new Exception("The folder being created already exists"); //TODO: Stop being lazy and write my own exceptions
                    }
                    await GithubProvider.Client.Repository.Content.CreateFile(
                        folder.Org,
                        folder.Name,
                        Path.Combine(folder.FilePath, ".gitkeep"),
                        new CreateFileRequest("Add .gitkeep", "")
                    );
                    return folder;
                } }
            };

        protected override void NewItem(string path, string itemTypeName, object newItemValue)
        {
            if (!itemCreationHandlers.ContainsKey(itemTypeName))
            {
                throw new NotSupportedException("The Github Provider does not know how to make that object!");
            }
            var item = itemCreationHandlers[itemTypeName](path, newItemValue).Resolve();
            if (item != null)
            {
                PathInfo.PathInfoCache.Remove(GetParentPath(path, null)); //invalidate cache of parent object
                WriteItemObject(item, path, /*???*/ false);
            }
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
            return path.LastIndexOf(Path.DirectorySeparatorChar) >= 0 ?
                path.Substring(path.LastIndexOf(Path.DirectorySeparatorChar)+1) : path;
        }

        protected static string StaticGetParentPath(string path, string root)
        {
            // If root is specified then the path has to contain
            // the root. If not nothing should be returned
            if (!String.IsNullOrEmpty(root))
            {
                if (!path.StartsWith(root))
                {
                    return null;
                }
            }

            var index = path.LastIndexOf(Path.DirectorySeparatorChar) >= 0 ? path.LastIndexOf(Path.DirectorySeparatorChar) : 0;
            return path.Substring(0, index);
        }

        protected override string GetParentPath(string path, string root)
        {
            return GithubProvider.StaticGetParentPath(path, root);
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

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
            return; //noop
        }

        public object ClearContentDynamicParameters(string path)
        {
            return null;
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
            var info = PathInfo.UncheckedFileFromPath(path);
            if (info != null)
            {
                if (new RepoInfo(info.Org, info.Repo).Exists().Resolve())
                {
                    var writer = new HttpFileWriter(async (data) =>
                    {
                        if (await info.Exists())
                        {
                            await GithubProvider.Client.Repository.Content.UpdateFile(
                                info.Org,
                                info.Repo,
                                info.FilePath,
                                new UpdateFileRequest(string.Concat("Update ", info.Name), System.Text.Encoding.UTF8.GetString(data), info.Sha));
                        } else
                        {
                            await GithubProvider.Client.Repository.Content.CreateFile(
                                info.Org,
                                info.Repo,
                                info.FilePath,
                                new CreateFileRequest(string.Concat("Add ", info.Name), System.Text.Encoding.UTF8.GetString(data)));

                        }
                        PathInfo.PathInfoCache.Remove(GetParentPath(path, null));
                    });
                    return writer;
                }
            }
            return null;
        }

        public object GetContentWriterDynamicParameters(string path)
        {
            return null;
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

        protected override void SetItem(string path, object value)
        {
            var content = fileDelegate(path, value).Resolve();
            if (content == null) return;
            WriteItemObject(content, path, false);
        }

        protected override void ClearItem(string path)
        {
            var item = FileInfo.FromFSPath(path).Resolve();
            if (item.Type == PathType.File || item.Type == PathType.Folder)
            {
                var file = item as FilesystemInfo;
                GithubProvider.Client.Repository.Content.DeleteFile(
                    file.Org,
                    file.Repo,
                    file.FilePath,
                    new DeleteFileRequest(string.Concat("Delete ", file.Name), file.Sha)
                ).Resolve();
            }
            else if (item.Type == PathType.Repo)
            {
                var repo = item as RepoInfo;
                GithubProvider.Client.Repository.Delete(repo.Org, repo.Name).Resolve();
            }
        }

        private static async Task<object> directoryDelegate(string path, object input)
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
                    switch (parentInfo.Type)
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

            try
            {
                await GithubProvider.Client.Repository.Content.CreateFile(
                    folder.Org,
                    folder.Repo,
                    Path.Combine(folder.FilePath, ".gitkeep"),
                    new CreateFileRequest("Add .gitkeep", "")
                );
            }
            catch (Octokit.NotFoundException e)
            {
                throw new Exception("Couldn't create directory - couldn't find the repo.", e);
            }
            return folder;
        }

        private static async Task<object> fileDelegate(string path, object input)
        {
            var info = PathInfo.UncheckedFileFromPath(path);
            if (info != null && info.Type == PathType.File)
            {
                if (await PathInfo.UncheckedRepoFromPath(Path.Combine(info.Org, info.Repo)).Exists())
                {
                    if (await info.Exists())
                    {
                        throw new Exception("File already exists. (Did you try to pipe to new-item? Use set-content instead.)");
                    }
                    else
                    {
                        await GithubProvider.Client.Repository.Content.CreateFile(
                            info.Org,
                            info.Repo,
                            info.FilePath,
                            new CreateFileRequest(string.Concat("Add ", info.Name), input.ToString()));
                        return info;
                    }
                }
            }
            return null;
        }

        protected delegate Task<object> itemCreationDelegate(string path, object input);

        protected Dictionary<string, itemCreationDelegate> itemCreationHandlers
            = new Dictionary<string, itemCreationDelegate>() {
                { "Directory",  directoryDelegate },
                { "File", fileDelegate }
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
                WriteItemObject(item, path, false);
            }
        }

        protected override void RemoveItem(string path, bool recurse)
        {
            var item = FileInfo.FromFSPath(path).Resolve();
            if (item == null || !item.Exists().Resolve())
            {
                throw new FileNotFoundException(string.Concat("File ", path, " does not exist."));
            }
            if (recurse)
            {
                foreach (var child in item.Children().Resolve())
                {
                    RemoveItem(child.VirtualPath, recurse);
                }
            }
            if (item.Type == PathType.File)
            {
                ClearItem(path);
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
                path.Substring(path.LastIndexOf(Path.DirectorySeparatorChar) + 1) : null;
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

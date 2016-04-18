using Octokit;
using Octokit.Caching;
using Octokit.Internal;
using System;
using System.Reflection;

namespace GithubProvider
{
    internal static class Static
    {
        private static GitHubClient _client;
        private static GitHubClient makeClient()
        {
            Assembly thisAssem = typeof(Static).Assembly;
            AssemblyName thisAssemName = thisAssem.GetName();
            Version ver = thisAssemName.Version;

            var header = new ProductHeaderValue(thisAssemName.Name, ver.ToString());

            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                return new GitHubClient(new Connection(header, new CachingHttpClient(new HttpClientAdapter(), new NaiveInMemoryCache())));
            }
            else
            {
                var connection = new Connection(
                    header,
                    GitHubClient.GitHubApiUrl,
                    new InMemoryCredentialStore(new Credentials(token)),
                    new CachingHttpClient(new HttpClientAdapter(), new NaiveInMemoryCache()),
                    new SimpleJsonSerializer());
                var client = new GitHubClient(connection);
                return client;
            }
        }
        public static GitHubClient Client
        {
            get
            {
                _client = _client ?? (_client = makeClient());
                return _client;
            }
        }

    }
}

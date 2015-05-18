using Octokit;
using Octokit.Caching;
using Octokit.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GithubProvider
{
    internal static class Static
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

    }
}

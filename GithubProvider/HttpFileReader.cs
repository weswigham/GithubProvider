using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Provider;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace GithubProvider
{
    public class HttpFileReader : IContentReader
    {
        private WebClient client;
        private Uri location;

        public HttpFileReader(Uri location)
        {
            this.location = location;
            client = new WebClient();
        }

        public void Close()
        {
            return;
        }

        public void Dispose()
        {
            client.Dispose();
        }

        private bool retrieved;

        public IList Read(long readCount)
        {
            if (retrieved)
            {
                return null;
            }
            retrieved = true;
            return new List<string>() { client.DownloadString(location) };
        }

        public void Seek(long offset, SeekOrigin origin)
        {
            return;
        }
    }
}

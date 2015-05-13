using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Provider;
using System.Text;
using System.Threading.Tasks;

namespace GithubProvider
{
    internal sealed class HttpFileWriter : IContentWriter
    {
        internal delegate void completeCallback(byte[] filedata);

        internal completeCallback Callback { get; private set; }
        internal byte[] Buffer { get; private set; }  = new byte[] { };
        public HttpFileWriter(completeCallback cb)
        {
            Callback = cb;
        }

        public void Close()
        {
            Callback(Buffer);
        }

        public void Dispose()
        {
            return;
        }

        public void Seek(long offset, SeekOrigin origin)
        {
            return;
        }

        public IList Write(IList content) //I have no idea what I am doing
        {
            foreach (var item in content)
            {
                if (item is string)
                {
                    var str = item as string;
                    Buffer = Buffer.Concat(Encoding.UTF8.GetBytes(str)).ToArray();
                } else if (item is byte[])
                {
                    var data = item as byte[];
                    Buffer = Buffer.Concat(data).ToArray();
                }

            }
            return new List<string>();
        }
    }
}

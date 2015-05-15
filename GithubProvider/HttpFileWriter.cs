using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Text;

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
            throw new PSNotSupportedException("Cannot seek while writing to github");
        }

        private static readonly byte[] newline = Encoding.UTF8.GetBytes("\n");

        public IList Write(IList content) //I have no idea what I am doing
        {
            if (content.Count <= 0)
            {
                return content;
            }

            if (content[0] is PSObject)
            {
                content = content.Cast<PSObject>().Select(obj => obj.BaseObject).ToArray();
            }

            foreach (var item in content)
            {
                if (item is string)
                {
                    var str = item as string;
                    Buffer = Buffer.Concat(Encoding.UTF8.GetBytes(str)).ToArray();
                    Buffer = Buffer.Concat(newline).ToArray();
                } else if (item is byte[])
                {
                    var data = item as byte[];
                    Buffer = Buffer.Concat(data).ToArray();
                } else if (item is byte)
                {
                    var data = (byte)item;
                    Buffer = Buffer.Concat(new byte[] { data }).ToArray();
                }

            }
            return content;
        }
    }
}

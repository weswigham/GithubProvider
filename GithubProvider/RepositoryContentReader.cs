using Microsoft.PowerShell.Commands;
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
    public sealed class RepositoryContentReader : IContentReader
    {
        private static char[] defaultDelimiter = new char[]{ '\n' };
        public string[] Lines {get; private set; }
        public byte[] Bytes { get; private set; }
        public RepositoryContentReader(IReadOnlyList<Octokit.RepositoryContent> data, string delimiter, FileSystemCmdletProviderEncoding enumCoding, Encoding encoding)
        {
            if (enumCoding == FileSystemCmdletProviderEncoding.Byte)
            {
                Bytes = data.Select(x => Convert.FromBase64String(x.EncodedContent)).Aggregate((left, right) => left.Concat(right).ToArray());
            }
            else
            {
                var content = data.Select(x => encoding.GetString(Convert.FromBase64String(x.EncodedContent))).Aggregate((total, x) => total + x);
                Lines = content.Split(delimiter?.ToCharArray() ?? defaultDelimiter);
            }
        }

        public void Close()
        {
        }

        public void Dispose()
        {
            Lines = null;
            Bytes = null;
        }

        private long position;

        public IList Read(long readCount)
        {
            if (Lines != null) {
                if (readCount <= 0)
                {
                    return Lines;
                }
                return Lines.Skip((int)position).Take((int)readCount).ToArray();
            }
            else
            {
                if (readCount <= 0)
                {
                    return Bytes;
                }
                return Bytes.Skip((int)position).Take((int)readCount).ToArray();
            }
        }

        public void Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    position = offset;
                    break;
                case SeekOrigin.Current:
                    position += offset;
                    break;
                case SeekOrigin.End:
                    if (Lines != null)
                    {
                        position = Lines.LongLength - offset;
                    }
                    else {
                        position = Bytes.LongLength - offset;
                    }
                    break;
            }
            if (position < 0)
                position = 0;
            if (Lines != null && position > Lines.LongLength)
            {
                position = Lines.LongLength;
            }
            else if (position > Bytes.LongLength) {
                position = Bytes.LongLength;
            }
        }
    }
}

using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;

namespace GithubProvider
{
    internal class GithubDriveInfo : PSDriveInfo
    {
        public GithubDriveInfo(PSDriveInfo info) : base(info)
        {

        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;


namespace GithubProvider.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "ProviderForUnresolvedPath")]
    public class GetProviderForUnresolvedPathCmdlet : PSCmdlet
    {
        [Parameter(Position = 0, Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public string Path { get; set; }
        protected override void EndProcessing()
        {
            ProviderInfo provider;
            PSDriveInfo drive;
            SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path, out provider, out drive);
            WriteObject(provider);
            base.EndProcessing();
        }
    }
}
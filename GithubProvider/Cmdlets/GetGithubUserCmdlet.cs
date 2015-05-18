using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;

namespace GithubProvider.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "GithubUser")]
    public class GetGithubUserCmdlet : Cmdlet
    {
        protected override void EndProcessing()
        {
            try {
                var user = Static.Client.User.Current().Resolve();
                WriteObject(user);
            } catch (Octokit.NotFoundException)
            {
                throw new Exception("Couldn't find the active github user. Is $env:GITHUB_TOKEN set?");
            } catch (Octokit.AuthorizationException)
            {
                throw new Exception("Denied the active github user. Is $env:GITHUB_TOKEN set to a token which allows user info?");
            }
            base.EndProcessing();
        }
    }
}

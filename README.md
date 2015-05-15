# GithubProvider [![Build status](https://ci.appveyor.com/api/projects/status/deuwe8iv0n67ufsb?svg=true)](https://ci.appveyor.com/project/weswigham/githubprovider)
A provider for powershell which uses octokit to let you use github repos as a filesystem inside powershell

##Usage
Install the `GithubFS` PSModule to a modules directory of your choice, then import it in your profile with
```powershell
ipmo GithubFS
```
Additionally, visit github and grab a [personal access token](https://github.com/settings/tokens). (You should 
probably give it user and repo permissions) Drop that in your profile like so:
```powershell
$env:GITHUB_TOKEN = '<token>';
```
Great! Now once you refresh your shell you should have access to a `GH:` drive which contains all the orgs/users you
know about! You should be able to use it just like it was a filesystem, however not all applications are capable
of using PSProviders for input paths.

##Detail
The `GithubProvider` project contains a few C# classes used to bind the Github API to a PSProvider and register
the provider.
The `GithubFS` project is a powershell module which loads the `GithubProvider` and, additionally, provides a few
useful cmdlets for manipulating and using the github psprovider. 

###Provided Cmdlets
####Out-Github
Is a proxy to `Out-File` which knows about the Github PSProvider and polyfills support for it using `set-content`
to recreate the unavailable stream support. It is aliased to override `Out-File` so that it polyfills support for 
shell redirections.

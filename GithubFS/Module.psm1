function Out-Github {
	[CmdletBinding(DefaultParameterSetName='ByPath', SupportsShouldProcess=$true, ConfirmImpact='Medium', HelpUri='http://go.microsoft.com/fwlink/?LinkID=113363')]
	param(
		[Parameter(ParameterSetName='ByPath', Mandatory=$true, Position=0)]
		[string]
		${FilePath},

		[Parameter(ParameterSetName='ByLiteralPath', Mandatory=$true, ValueFromPipelineByPropertyName=$true)]
		[Alias('PSPath')]
		[string]
		${LiteralPath},

		[Parameter(Position=1)]
		[ValidateSet('unknown','string','unicode','bigendianunicode','utf8','utf7','utf32','ascii','default','oem')]
		[ValidateNotNullOrEmpty()]
		[string]
		${Encoding},

		[switch]
		${Append},

		[switch]
		${Force},

		[Alias('NoOverwrite')]
		[switch]
		${NoClobber},

		[ValidateRange(2, 2147483647)]
		[int]
		${Width},

		[switch]
		${NoNewline},

		[Parameter(ValueFromPipeline=$true)]
		[psobject]
		${InputObject})

	begin
	{
		[object[]] $data = @();
		$github = $false
		if ( $(get-item $FilePath.Substring(0,$FilePath.LastIndexOf(':')+1)).PSProvider.Name -eq 'Github' ) {
			$github = $true;
		} else {
		
			try {
				$outBuffer = $null
				if ($PSBoundParameters.TryGetValue('OutBuffer', [ref]$outBuffer))
				{
					$PSBoundParameters['OutBuffer'] = 1
				}
				$wrappedCmd = $ExecutionContext.InvokeCommand.GetCommand('Microsoft.PowerShell.Utility\Out-File', [System.Management.Automation.CommandTypes]::Cmdlet)
				$scriptCmd = {& $wrappedCmd @PSBoundParameters }
				$steppablePipeline = $scriptCmd.GetSteppablePipeline($myInvocation.CommandOrigin)
				$steppablePipeline.Begin($PSCmdlet)
			} catch {
				throw
			}
		}
	}
	
	process
	{
		if ($github -eq $true) {
			$data += $_
		} else {
			try {
				$steppablePipeline.Process($_)
			} catch {
				throw
			}
		}
	}

	end
	{
		if ($github -eq $true) {
			Set-Content $FilePath $data
		} else {
			try {
				$steppablePipeline.End()
			} catch {
				throw
			}
		}
	}
	<#

	.ForwardHelpTargetName Microsoft.PowerShell.Utility\Out-File
	.ForwardHelpCategory Cmdlet

	#>
}

New-Alias -Name 'Out-File' -Value 'Out-Github' -Scope Global
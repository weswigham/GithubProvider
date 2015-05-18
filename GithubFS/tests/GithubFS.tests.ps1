ipmo "$PSScriptRoot/../GithubFS.psd1" -Force

$pathchar = [IO.Path]::DirectorySeparatorChar;
$testbotAccountName = 'repotestbot';
$testbotRepo = 'scratch';
$workdir = "GH:$pathchar$testbotAccountName$pathchar$testbotRepo";
$readme = 'README.md';

Describe "GithubFS" {
    Context "Integrating with cmdlets" {
        It "Lets you use github as a browseable fs" {
            pushd $workdir;
            pwd | should be $workdir;
            popd
        }
        
        It "Lets you read the examine the existance of a file" {
            "$workdir$pathchar$readme" | Should Exist;
        }        
        
        It "Lets you read the contents of a file" {
            "$workdir$pathchar$readme" | Should Contain "# $testbotRepo";
        }
        
        It "Enables redirection into the github fs to create or edit files" {
            $testString = @"
This is a content
pipe test
"@;
            $filename = 'test.txt';
            $filepath = "$workdir$pathchar$filename";
            rm $filepath -Force -Recurse -ErrorAction SilentlyContinue
            $testString | out-github $filepath
            $filepath | Should Exist;
            $content = cat $filepath;
            $content | Should Be "$testString`n"; #Pipe adds a newline to the end
            rm $filepath;
        }
        
        It "Enables the creation or deletion of files in github" {
            $filename = 'newitem.txt';
            $filepath = "$workdir$pathchar$filename";
            rm $filepath -Force -Recurse -ErrorAction SilentlyContinue
            new-item -Type File -Path $filepath -Value "content";
            cat $filepath | Should Be "content";
            rm $filepath;
            $filepath | Should Not Exist;
        }
        
        It "Enables the creation or deletion of repos in github" {
            $reponame = 'scratch2';
            $repo = "GH:$pathchar$testbotAccountName$pathchar$reponame";
            rm $repo -Force -Recurse -ErrorAction SilentlyContinue
            mkdir $repo;
            $repo | Should Exist;
            rm $repo -r;
            $repo | Should Not Exist;
        }
        
        It "Can make files with mkdir and remove folders" {
            $foldername = 'testfolder';
            $gitkeep = '.gitkeep';
            $directory = "$workdir$pathchar$foldername";
            rm $directory -Force -Recurse -ErrorAction SilentlyContinue
            mkdir $directory;
            "$directory$pathchar$gitkeep" | Should Exist;
            rm $directory -r;
            $directory | Should Not Exist;
        }
    }
    
    Context "Browsing github" {
        It "can browse repos from orgs you're not a member of" {
            $ms = 'Microsoft';
            ls "GH:$pathchar$ms" | Should Not BeNullOrEmpty
        }
    }
}

ipmo ../../GithubFS -Force

$testbotAccountName = 'repotestbot';
$testbotRepo = 'scratch';
$workdir = "GH:/$testbotAccountName/$testbotRepo";

Describe "GithubFS" {
	Context "Integrating with cmdlets" {
		It "Lets you use github as a browseable fs" {
			pushd $workdir;
			pwd | should be $workdir;
			popd
		}
		
		It "Lets you read the examine the existance of a file" {
			"$workdir/README.md" | Should Exist;
		}		
		
		It "Lets you read the contents of a file" {
			cat "$workdir/README.md" | Should Contain "# scratch";
		}
		
		It "Enables redirection into the github fs to create or edit files" {
			$testString = "This is a content pipe test";
			echo $testString > "$workdir/test.txt";
			cat "$workdir/test.txt" | Should Be $testString;
			rm "$workdir/test.txt"
		}
		
		It "Enables the creation or deletion of files in github" {
			new-item -Type File -Path "$workdir/newitem.txt" -Value "content";
			cat "$workdir/newitem.txt" | Should Be "content";
			rm "$workdir/newitem.txt";
			"$workdir/newitem.txt" | Should Not Exist;
		}
		
		It "Enables the creation or deletion of repos in github" {
			$repo = "GH:\$testbotAccountName\scratch2";
			mkdir $repo;
			$repo | Should Exist;
			rm $repo;
			$repo | Should Not Exist;
		}
		
		It "Can make files with mkdir and remove folders" {
			$directory = "$workdir/testfolder"; 
			mkdir $directory;
			"$directory/.gitkeep" | Should Exist;
			rm $directory;
			$directory | Should Not Exist;
		}
	}
	
	Context "Browsing github" {
		It "can browse repos from orgs you're not a member of" {
			ls "GH:\Microsoft" | Should Not BeNullOrEmpty
		}
	}
}
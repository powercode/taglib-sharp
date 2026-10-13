param(
	[ValidateSet("Debug", "Release")]
	[string] $Configuration = "Release"
)

dotnet build .\src\TaglibSharp -c $Configuration

Push-Location .\tests\TaglibSharp.Tests
try{
	dotnet test -c Debug --verbosity minimal TaglibSharp.Tests.csproj
}
finally{
	Pop-Location
}

if ($LASTExitCode -eq 0) {
	dotnet pack .\src\TaglibSharp --no-build -c $Configuration

	Get-Item .\artifacts\package\$Configuration\*
}
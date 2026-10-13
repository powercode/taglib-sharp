param(
	[ValidateSet("Debug", "Release")]
	[string] $Configuration = "Release"
)

dotnet build .\src\TaglibSharp -c $Configuration

dotnet test -c Debug  .\tests\TaglibSharp.Tests\TaglibSharp.Tests.csproj

if ($?) {
	dotnet pack .\src\TaglibSharp --no-build -c $Configuration
}
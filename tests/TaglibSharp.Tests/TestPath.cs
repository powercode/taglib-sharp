using System.Reflection;

namespace TaglibSharp.Tests;

/// <summary>
/// OS-Independent Path composition
/// </summary>
public static class TestPath
{
	static readonly string TestProjectDirectory = typeof(TestPath).Assembly
		.GetCustomAttributes<AssemblyMetadataAttribute>()
		.Single(attribute => attribute.Key == "TestProjectDirectory").Value
		?? throw new InvalidOperationException("TestProjectDirectory assembly metadata is missing a value.");

	public static string TestsDir { get; } = Path.GetDirectoryName(typeof(TestPath).Assembly.Location);
	public static string Samples { get; } = Path.Combine(TestProjectDirectory, "samples") + Path.DirectorySeparatorChar;
	public static string SamplesTmp { get; } = Directory.CreateDirectory(Path.Combine(Samples, Environment.Version.ToString())).FullName + Path.DirectorySeparatorChar;
	public static string RawSamples { get; } = Path.Combine(TestProjectDirectory, "raw-samples") + Path.DirectorySeparatorChar;
	public static string GetRawSubDirectory(string subdir) => Path.Combine(RawSamples, subdir);
	public static string Covers { get; } = Path.GetFullPath(Path.Combine(TestProjectDirectory, "..", "..", "examples", "covers")) + Path.DirectorySeparatorChar;
}
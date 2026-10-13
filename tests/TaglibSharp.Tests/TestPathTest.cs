namespace TaglibSharp.Tests;

[TestClass]
public class TestPathTest
{
	[TestMethod]
	public void ResolveSourceDataDirectories ()
	{
		Assert.IsTrue (System.IO.File.Exists (TestPath.Samples + "sample.wav"));
		Assert.IsTrue (System.IO.File.Exists (TestPath.Covers + "sample_a.png"));
		Assert.IsTrue (Directory.Exists (TestPath.RawSamples));
	}

	[TestMethod]
	public void PreserveTrailingDirectorySeparators ()
	{
		foreach (var path in new[] { TestPath.Samples, TestPath.SamplesTmp, TestPath.RawSamples, TestPath.Covers }) {
			Assert.IsTrue (Path.IsPathFullyQualified (path));
			Assert.EndsWith (Path.DirectorySeparatorChar.ToString (), path);
		}
	}

	[TestMethod]
	public void KeepTemporarySamplesSeparatedByRuntime ()
	{
		Assert.AreEqual (
			Path.Combine (TestPath.Samples, Environment.Version.ToString ()) + Path.DirectorySeparatorChar,
			TestPath.SamplesTmp);
		Assert.IsTrue (Directory.Exists (TestPath.SamplesTmp));
	}
}

using TagLib.Id3v2;

namespace TaglibSharp.Tests.TaggingFormats;

[TestClass]
public class TextInformationFrameTest
{
	[TestMethod]
	[DataRow (StringType.Latin1)]
	[DataRow (StringType.UTF8)]
	[DataRow (StringType.UTF16)]
	[DataRow (StringType.UTF16BE)]
	[DataRow (StringType.UTF16LE)]
	public void NullSeparatedFieldsPreserveInteriorEmptyFields (StringType type)
	{
		foreach (var (id, version) in new[] { ("TXXX", (byte)2), ("TXXX", (byte)3), ("TXXX", (byte)4), ("TIT2", (byte)4) }) {
			foreach (string text in new[] { "", "\0", "\0\0", "a", "a\0", "a\0\0", "\0a\0\0b\0\0" }) {
				var frame = new RawTextFrame (id);
				frame.Load (Payload (text, type), version);
				string[] expected = text.Split ('\0');
				int count = expected.Length;
				while (count != 0 && expected[count - 1].Length == 0)
					--count;
				Array.Resize (ref expected, count);

				Assert.AreSequenceEqual (expected, frame.Text, $"ID={id}, version={version}, text={text}");
				Assert.AreSequenceEqual (expected, frame.Text);
			}
		}
	}

	[TestMethod]
	[DataRow ("TIT2", "a/b;c", new[] { "a/b;c" })]
	[DataRow ("TIT2", "title\0ignored", new[] { "title" })]
	[DataRow ("TIT2", "\0ignored", new string[0])]
	[DataRow ("TIT2", "", new string[0])]
	[DataRow ("TPE1", "/a//b///", new[] { "", "a", "", "b" })]
	[DataRow ("TPE1", "a/b\0ignored", new[] { "a", "b" })]
	[DataRow ("TPE1", "///", new string[0])]
	[DataRow ("TCON", "(17)Rock/(8)Jazz/Other;More//", new[] { "17", "8", "Other", "More" })]
	[DataRow ("TCON", "(17)/(8)", new[] { "17", "8" })]
	[DataRow ("TCON", "(17)rock", new[] { "17", "rock" })]
	[DataRow ("TCON", "(999)Unknown", new[] { "999", "Unknown" })]
	[DataRow ("TCON", "(RX)Remix", new[] { "RX", "Remix" })]
	[DataRow ("TCON", "(Rock", new[] { "(Rock" })]
	[DataRow ("TCON", "((Rock)", new[] { "(Rock" })]
	[DataRow ("TCON", "()Custom", new[] { "", "Custom" })]
	[DataRow ("TCON", "(17) / Rock / ;Other", new[] { "17", "", "Other" })]
	[DataRow ("TCON", "Rock;;Jazz/\0ignored", new[] { "Rock", "", "Jazz" })]
	public void LegacyFieldsPreserveParsingRules (string id, string text, string[] expected)
	{
		foreach (byte version in new byte[] { 2, 3 }) {
			foreach (StringType type in new[] { StringType.Latin1, StringType.UTF8, StringType.UTF16, StringType.UTF16BE, StringType.UTF16LE }) {
				var frame = new RawTextFrame (id);
				frame.Load (Payload (text, type), version);
				Assert.AreSequenceEqual (expected, frame.Text, $"ID={id}, version={version}, encoding={type}");
			}
		}
	}

	[TestMethod]
	[DataRow ("TCOM")]
	[DataRow ("TEXT")]
	[DataRow ("TMCL")]
	[DataRow ("TOLY")]
	[DataRow ("TOPE")]
	[DataRow ("TSOC")]
	[DataRow ("TSOP")]
	[DataRow ("TSO2")]
	[DataRow ("TPE1")]
	[DataRow ("TPE2")]
	[DataRow ("TPE3")]
	[DataRow ("TPE4")]
	[DataRow ("MVNM")]
	[DataRow ("MVIN")]
	public void LegacySlashSeparatedFramesPreserveEmptyFields (string id)
	{
		var frame = new RawTextFrame (id);
		frame.Load (Payload ("one//two/three;", StringType.UTF8), 3);
		Assert.AreSequenceEqual (new[] { "one", "", "two", "three;" }, frame.Text);
	}

	[TestMethod]
	public void LegacyUtf16PreservesIncompleteDelimiter ()
	{
		var frame = new RawTextFrame ("TIT2");
		frame.Load (new ByteVector ((byte)StringType.UTF16, 0), 3);
		Assert.AreSequenceEqual (new[] { "\ufffd" }, frame.Text);
	}

	[TestMethod]
	[DataRow ("TXXX", (byte)3)]
	[DataRow ("TIT2", (byte)4)]
	public void ParsingReusesDecoderArrayAndRunsOnlyOnce (string id, byte version)
	{
		var payload = Payload ("description\0one\0two", StringType.UTF8);
		var frame = new RawTextFrame (id);
		for (int i = 0; i < 100; ++i) {
			payload.ToStrings (StringType.UTF8, 1);
			frame.Load (payload, version);
			frame.Parse ();
		}

		long before = GC.GetAllocatedBytesForCurrentThread ();
		var baseline = payload.ToStrings (StringType.UTF8, 1);
		long baselineAllocation = GC.GetAllocatedBytesForCurrentThread () - before;

		frame.Load (payload, version);
		before = GC.GetAllocatedBytesForCurrentThread ();
		frame.Parse ();
		long allocation = GC.GetAllocatedBytesForCurrentThread () - before;
		Assert.AreEqual (baselineAllocation, allocation);

		before = GC.GetAllocatedBytesForCurrentThread ();
		frame.Parse ();
		Assert.AreEqual (0L, GC.GetAllocatedBytesForCurrentThread () - before);
		Assert.AreSequenceEqual (baseline, frame.Text);

		var copy = frame.Text;
		copy[0] = "changed";
		Assert.AreSequenceEqual (baseline, frame.Text);
		GC.KeepAlive (baseline);
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void LegacyParsingAllocatesLessThanSubstringAndSplitParsing (bool genre)
	{
		var payload = Payload (genre ? "(17)Rock/(8)Jazz/Other;More\0ignored"
			: "one/two/three/four\0ignored", StringType.UTF8);
		var frame = new RawTextFrame (genre ? "TCON" : "TPE1");
		for (int i = 0; i < 100; ++i) {
			ParseLegacyReference (payload, genre);
			frame.Load (payload, 3);
			frame.Parse ();
		}

		long before = GC.GetAllocatedBytesForCurrentThread ();
		var baseline = ParseLegacyReference (payload, genre);
		long baselineAllocation = GC.GetAllocatedBytesForCurrentThread () - before;

		frame.Load (payload, 3);
		before = GC.GetAllocatedBytesForCurrentThread ();
		frame.Parse ();
		long allocation = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsLessThan (baselineAllocation, allocation);
		Assert.AreSequenceEqual (baseline, frame.Text);
		GC.KeepAlive (baseline);
	}

	static string[] ParseLegacyReference (ByteVector payload, bool genre)
	{
		var fields = new List<string> ();
		string value = payload.ToString (StringType.UTF8, 1, payload.Count - 1);
		int nullIndex = value.IndexOf ('\0');
		if (nullIndex >= 0)
			value = value.Substring (0, nullIndex);
		if (genre) {
			while (value.Length > 1 && value[0] == '(') {
				int closing = value.IndexOf (')');
				if (closing < 0)
					break;
				string number = value.Substring (1, closing - 1);
				fields.Add (number);
				value = value.Substring (closing + 1).TrimStart ('/', ' ');
				string text = Genres.IndexToAudio (number);
				if (text != null && value.StartsWith (text))
					value = value.Substring (text.Length).TrimStart ('/', ' ');
			}
			if (value.Length > 0)
				fields.AddRange (value.Split (new[] { '/', ';' }));
		} else {
			fields.AddRange (value.Split ('/'));
		}
		while (fields.Count != 0 && string.IsNullOrEmpty (fields[fields.Count - 1]))
			fields.RemoveAt (fields.Count - 1);
		return fields.ToArray ();
	}

	static ByteVector Payload (string text, StringType type)
	{
		var payload = new ByteVector ((byte)type);
		payload.Add (ByteVector.FromString (text, type));
		return payload;
	}

	sealed class RawTextFrame : TextInformationFrame
	{
		public RawTextFrame (string id) : base (id, StringType.UTF8) { }
		public void Load (ByteVector payload, byte version) => ParseFields (payload, version);
		public void Parse () => ParseRawData ();
	}
}

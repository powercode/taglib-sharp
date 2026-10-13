using System.Security.Cryptography;
using System.Reflection;
using System.Text;

namespace TaglibSharp.Tests.Collections;

[TestClass]
public class ByteVectorTest
{
	static readonly string TestInput = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
	static readonly ByteVector TestVector = ByteVector.FromString (TestInput, StringType.UTF8);

	[TestMethod]
	public void Length ()
	{
		Assert.AreEqual (TestInput.Length, TestVector.Count);
	}

	[TestMethod]
	[DataRow (StringType.Latin1)]
	[DataRow (StringType.UTF8)]
	[DataRow (StringType.UTF16)]
	[DataRow (StringType.UTF16BE)]
	[DataRow (StringType.UTF16LE)]
	public void FromStringMatchesEncoding (StringType type)
	{
		bool previous = ByteVector.UseBrokenLatin1Behavior;
		try {
			ByteVector.UseBrokenLatin1Behavior = false;
			Encoding encoding = type switch {
				StringType.Latin1 => Encoding.Latin1,
				StringType.UTF8 => Encoding.UTF8,
				StringType.UTF16BE => Encoding.BigEndianUnicode,
				_ => Encoding.Unicode
			};

			foreach (string text in new[] { null, "", "A", "A\u00e9\u20ac\U0001f600Z", "\ud800X\udc00", new string ('\u20ac', 4096) }) {
				foreach (int length in new[] { 0, 1, 2, 4, int.MaxValue }) {
					string truncated = text == null ? "" : text.Substring (0, Math.Min (text.Length, length));
					byte[] payload = encoding.GetBytes (truncated);
					byte[] expected = type == StringType.UTF16
						? new byte[] { 0xff, 0xfe }.Concat (payload).ToArray ()
						: payload;

					var vector = ByteVector.FromString (text, type, length);
					Assert.AreSequenceEqual (expected, vector.Data, $"Type={type}, length={length}");

					var field = typeof (ByteVector).GetField ("data", BindingFlags.Instance | BindingFlags.NonPublic);
					var storage = (List<byte>)field.GetValue (vector);
					Assert.AreEqual (expected.Length, storage.Capacity, $"Type={type}, length={length}");
				}
			}
		} finally {
			ByteVector.UseBrokenLatin1Behavior = previous;
		}
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void FromStringEncodingTestRestoresLatin1Behavior (bool useBrokenLatin1)
	{
		bool previous = ByteVector.UseBrokenLatin1Behavior;
		try {
			ByteVector.UseBrokenLatin1Behavior = useBrokenLatin1;
			FromStringMatchesEncoding (StringType.Latin1);
			Assert.AreEqual (useBrokenLatin1, ByteVector.UseBrokenLatin1Behavior);
		} finally {
			ByteVector.UseBrokenLatin1Behavior = previous;
		}
	}

	[TestMethod]
	public void FromStringPreservesLengthBehavior ()
	{
		Assert.AreEqual ("length", Assert.ThrowsExactly<ArgumentOutOfRangeException> (
			() => ByteVector.FromString ("A", StringType.UTF8, -1)).ParamName);
		Assert.AreEqual (0, ByteVector.FromString (null, StringType.UTF8, -1).Count);
		Assert.AreEqual (0, ByteVector.FromString ("", StringType.UTF8, -1).Count);
		CollectionAssert.AreEqual (new byte[] { 0xff, 0xfe },
			ByteVector.FromString (null, StringType.UTF16, -1).Data);
		CollectionAssert.AreEqual (new byte[] { 0xff, 0xfe },
			ByteVector.FromString ("", StringType.UTF16, -1).Data);
		CollectionAssert.AreEqual (Encoding.UTF8.GetBytes ("AB"), ByteVector.FromString ("ABC", 2).Data);
	}

	[TestMethod]
	public void FromStringPreservesUtf16DecodingState ()
	{
		var bigEndian = new ByteVector (0xfe, 0xff, 0, 65);
		var withoutBom = new ByteVector (new byte[] { 0, 65 });
		Assert.AreEqual ("A", bigEndian.ToString (StringType.UTF16));
		ByteVector.FromString (null, StringType.UTF16);
		ByteVector.FromString ("", StringType.UTF16);
		Assert.AreEqual ("A", withoutBom.ToString (StringType.UTF16));

		ByteVector.FromString ("A", StringType.UTF16, 0);
		Assert.AreEqual ("A", new ByteVector (new byte[] { 65, 0 }).ToString (StringType.UTF16));
	}

	[TestMethod]
	public void SpanConstructorCopiesData ()
	{
		Span<byte> source = stackalloc byte[] { 1, 2, 3 };
		var vector = new ByteVector (source);
		source[0] = 4;

		CollectionAssert.AreEqual (new byte[] { 1, 2, 3 }, vector.Data);
		var field = typeof (ByteVector).GetField ("data", BindingFlags.Instance | BindingFlags.NonPublic);
		var storage = (List<byte>)field.GetValue (vector);
		Assert.AreEqual (source.Length, storage.Capacity);
		Assert.AreEqual (0, new ByteVector (ReadOnlySpan<byte>.Empty).Count);
	}

	[TestMethod]
	[DataRow (StringType.Latin1)]
	[DataRow (StringType.UTF8)]
	[DataRow (StringType.UTF16)]
	[DataRow (StringType.UTF16BE)]
	[DataRow (StringType.UTF16LE)]
	public void FromStringAcceptsSpans (StringType type)
	{
		ReadOnlySpan<char> text = "XA\u00e9\u20ac\U0001f600Z".AsSpan (1, 5);
		CollectionAssert.AreEqual (ByteVector.FromString (text.ToString (), type).Data,
			ByteVector.FromString (text, type).Data);
		CollectionAssert.AreEqual (ByteVector.FromString (text.ToString (), type, 4).Data,
			ByteVector.FromString (text, type, 4).Data);
		CollectionAssert.AreEqual (ByteVector.FromString ("", type).Data,
			ByteVector.FromString (ReadOnlySpan<char>.Empty, type).Data);
	}

	[TestMethod]
	[DataRow (StringType.UTF8, 100)]
	[DataRow (StringType.UTF8, int.MaxValue)]
	[DataRow (StringType.UTF16, 100)]
	[DataRow (StringType.UTF16, int.MaxValue)]
	public void FromStringAllocatesOnlyFinalStorage (StringType type, int length)
	{
		string text = new string ('\u20ac', 4096);
		byte[] expected = ByteVector.FromString (text, type, length).Data;
		for (int i = 0; i < 100; ++i) {
			ByteVector.FromString (text, type, length);
			_ = new ByteVector (expected);
		}

		long before = GC.GetAllocatedBytesForCurrentThread ();
		var baseline = new ByteVector (expected);
		long baselineAllocation = GC.GetAllocatedBytesForCurrentThread () - before;

		before = GC.GetAllocatedBytesForCurrentThread ();
		var vector = ByteVector.FromString (text, type, length);
		long allocation = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.AreEqual (baselineAllocation, allocation);
		CollectionAssert.AreEqual (expected, vector.Data);
		GC.KeepAlive (baseline);
	}

	[TestMethod]
	public void StartsWith ()
	{
		Assert.IsTrue (TestVector.StartsWith ("ABCDE"));
		Assert.IsFalse (TestVector.StartsWith ("NOOP"));
	}

	[TestMethod]
	public void EndsWith ()
	{
		Assert.IsTrue (TestVector.EndsWith ("UVWXYZ"));
		Assert.IsFalse (TestVector.EndsWith ("NOOP"));
	}

	[TestMethod]
	public void ContainsAt ()
	{
		Assert.IsTrue (TestVector.ContainsAt ("JKLMNO", 9));
		Assert.IsFalse (TestVector.ContainsAt ("NOOP", 30));
	}

	[TestMethod]
	public void Find ()
	{
		Assert.AreEqual (17, TestVector.Find ("RSTUV"));
		Assert.AreEqual (-1, TestVector.Find ("NOOP"));
	}

	[TestMethod]
	[DataRow ("ABCABC", "A", 0, 1, 0)]
	[DataRow ("ABCABC", "A", 1, 1, 3)]
	[DataRow ("ABCABC", "C", 4, 1, 5)]
	[DataRow ("ABCABC", "D", 0, 1, -1)]
	[DataRow ("ABCABC", "ABC", 1, 1, 3)]
	[DataRow ("ABCABC", "BC", 4, 1, 4)]
	[DataRow ("ABCABC", "ABCD", 3, 1, -1)]
	[DataRow ("ABCABC", "A", 6, 1, -1)]
	[DataRow ("ABCABC", "ABC", 7, 1, -1)]
	[DataRow ("ABCABC", "", 0, 1, 0)]
	[DataRow ("ABCABC", "", 3, 1, 3)]
	[DataRow ("ABCABC", "", 6, 1, 6)]
	[DataRow ("ABCABC", "", 7, 1, -1)]
	[DataRow ("", "", 0, 1, 0)]
	[DataRow ("", "A", 0, 1, -1)]
	[DataRow ("ABCABC", "A", 1, 2, 3)]
	[DataRow ("ABCABC", "A", 0, 2, 0)]
	[DataRow ("ABCABC", "BC", 0, 2, 4)]
	[DataRow ("ABCABC", "BC", 1, 2, 1)]
	[DataRow ("ABCABC", "", 3, 2, 3)]
	[DataRow ("BAAAAA", "AAA", 0, 2, 2)]
	[DataRow ("BAAAAA", "AAA", 1, 3, 1)]
	public void FindWithOffsetAndAlignment (string source, string pattern, int offset, int alignment, int expected)
	{
		var vector = ByteVector.FromString (source, StringType.UTF8);
		var needle = ByteVector.FromString (pattern, StringType.UTF8);

		Assert.AreEqual (expected, vector.Find (needle, offset, alignment));
	}

	[TestMethod]
	public void FindValidatesArguments ()
	{
		Assert.AreEqual ("pattern", Assert.ThrowsExactly<ArgumentNullException> (() => TestVector.Find (null, 0, 1)).ParamName);
		Assert.AreEqual ("offset", Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => TestVector.Find ("A", -1, 1)).ParamName);
		Assert.AreEqual ("byteAlign", Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => TestVector.Find ("A", 0, 0)).ParamName);
		Assert.AreEqual ("byteAlign", Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => TestVector.Find ("A", 0, -1)).ParamName);
	}

	[TestMethod]
	public void FindMatchesScalarSearch ()
	{
		var random = new Random (42);
		foreach (int length in new[] { 0, 1, 15, 16, 17, 31, 32, 33, 63, 64, 65, 257 }) {
			var source = new byte[length];
			random.NextBytes (source);
			var vector = new ByteVector (source);

			foreach (int patternLength in new[] { 0, 1, 2, 3, 16, 33, 65, 258 }) {
				var pattern = new byte[patternLength];
				random.NextBytes (pattern);
				for (int pass = 0; pass < 2; ++pass) {
					if (pass == 1 && patternLength <= length)
						Array.Copy (source, length - patternLength, pattern, 0, patternLength);

					var needle = new ByteVector (pattern);
					foreach (int offset in new[] { 0, 1, length / 2, length, length + 1 }) {
						foreach (int alignment in new[] { 1, 2, 3, 8 }) {
							int expected = -1;
							for (int i = offset; i <= length - patternLength; i += alignment) {
								int j = 0;
								while (j < patternLength && source[i + j] == pattern[j])
									++j;

								if (j == patternLength) {
									expected = i;
									break;
								}
							}

							Assert.AreEqual (expected, vector.Find (needle, offset, alignment),
								$"Length={length}, pattern={patternLength}, offset={offset}, alignment={alignment}, pass={pass}");
						}
					}
				}
			}
		}
	}

	[TestMethod]
	public void RFind ()
	{
		Assert.AreEqual (6, TestVector.RFind ("GHIJ"));
		Assert.AreEqual (-1, TestVector.RFind ("NOOP"));
	}

	[TestMethod]
	[DataRow ("ABCABC", "A", 0, 1, 3)]
	[DataRow ("ABCABC", "A", 3, 1, 0)]
	[DataRow ("ABCABC", "C", 1, 1, 2)]
	[DataRow ("ABCABC", "D", 0, 1, -1)]
	[DataRow ("ABCABC", "ABC", 0, 1, 3)]
	[DataRow ("ABCABC", "ABC", 1, 1, 0)]
	[DataRow ("ABCABC", "BC", 0, 1, 4)]
	[DataRow ("ABCABC", "BC", 1, 1, 1)]
	[DataRow ("ABCABC", "ABCD", 0, 1, -1)]
	[DataRow ("ABCABC", "ABCABC", 0, 1, 0)]
	[DataRow ("ABCABC", "ABCABC", 1, 1, -1)]
	[DataRow ("ABCABC", "A", 6, 1, -1)]
	[DataRow ("ABCABC", "ABC", 7, 1, -1)]
	[DataRow ("ABCABC", "A", int.MaxValue, 1, -1)]
	[DataRow ("ABCABC", "", 0, 1, -1)]
	[DataRow ("ABCABC", "", 6, 1, -1)]
	[DataRow ("", "", 0, 1, -1)]
	[DataRow ("", "A", 0, 1, -1)]
	[DataRow ("AAAAA", "AAA", 0, 1, 2)]
	[DataRow ("AAAAA", "AAA", 1, 1, 1)]
	[DataRow ("ABCABC", "C", 0, 2, 5)]
	[DataRow ("ABCABC", "A", 1, 2, 0)]
	[DataRow ("ABCABC", "BC", 0, 2, 4)]
	[DataRow ("ABCABC", "BC", 1, 2, 1)]
	[DataRow ("BAAAAA", "AAA", 0, 2, 2)]
	public void RFindWithOffsetAndAlignment (string source, string pattern, int offset, int alignment, int expected)
	{
		var vector = ByteVector.FromString (source, StringType.UTF8);
		var needle = ByteVector.FromString (pattern, StringType.UTF8);

		Assert.AreEqual (expected, vector.RFind (needle, offset, alignment));
	}

	[TestMethod]
	public void RFindValidatesArguments ()
	{
		Assert.AreEqual ("pattern", Assert.ThrowsExactly<ArgumentNullException> (() => TestVector.RFind (null, 0, 1)).ParamName);
		Assert.AreEqual ("offset", Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => TestVector.RFind ("A", -1, 1)).ParamName);
	}

	[TestMethod]
	[DataRow ("A", 0)]
	[DataRow ("A", -1)]
	[DataRow ("AB", 0)]
	[DataRow ("AB", -1)]
	[DataRow ("", 0)]
	[DataRow ("", -1)]
	public void RFindRejectsInvalidAlignment (string pattern, int alignment)
	{
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException> (
			() => TestVector.RFind (pattern, 0, alignment));
		Assert.AreEqual ("byteAlign", exception.ParamName);
		Assert.AreEqual (alignment, exception.ActualValue);
	}

	[TestMethod]
	public void RFindMatchesScalarSearch ()
	{
		var random = new Random (42);
		foreach (int length in new[] { 0, 1, 15, 16, 17, 31, 32, 33, 63, 64, 65, 257 }) {
			var source = new byte[length];
			random.NextBytes (source);
			var vector = new ByteVector (source);

			foreach (int patternLength in new[] { 0, 1, 2, 3, 16, 33, 65, 258 }) {
				var pattern = new byte[patternLength];
				random.NextBytes (pattern);
				for (int pass = 0; pass < 2; ++pass) {
					if (pass == 1 && patternLength <= length)
						Array.Copy (source, (length - patternLength) / 2, pattern, 0, patternLength);

					var needle = new ByteVector (pattern);
					foreach (int offset in new[] { 0, 1, length / 2, length, length + 1 }) {
						int expected = -1;
						for (int i = length - offset - patternLength; patternLength > 0 && i >= 0; --i) {
							int j = 0;
							while (j < patternLength && source[i + j] == pattern[j])
								++j;

							if (j == patternLength) {
								expected = i;
								break;
							}
						}

						Assert.AreEqual (expected, vector.RFind (needle, offset, 1),
							$"Length={length}, pattern={patternLength}, offset={offset}, pass={pass}");
					}
				}
			}
		}
	}

	[TestMethod]
	public void Mid ()
	{
		Assert.AreEqual (ByteVector.FromString ("KLMNOPQRSTUVWXYZ", StringType.UTF8), TestVector.Mid (10));
		Assert.AreEqual (ByteVector.FromString ("PQRSTU", StringType.UTF8), TestVector.Mid (15, 6));
	}

	[TestMethod]
	[DataRow (-1, 0, "startIndex", "offset", -1)]
	[DataRow (27, 0, "startIndex", "offset", 27)]
	[DataRow (int.MaxValue, 0, "startIndex", "offset", int.MaxValue)]
	[DataRow (0, -1, "length", "count", -1)]
	[DataRow (0, 27, "length", "count", 27)]
	[DataRow (26, 1, "length", "count", 1)]
	[DataRow (1, int.MaxValue, "length", "count", int.MaxValue)]
	[DataRow (26, int.MaxValue, "length", "count", int.MaxValue)]
	[DataRow (-1, -1, "startIndex", "offset", -1)]
	public void RangesValidateArguments (int offset, int count, string midParameter, string stringParameter, int expectedValue)
	{
		var midException = Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => TestVector.Mid (offset, count));
		Assert.AreEqual (midParameter, midException.ParamName);
		Assert.AreEqual (expectedValue, midException.ActualValue);

		var stringException = Assert.ThrowsExactly<ArgumentOutOfRangeException> (
			() => TestVector.ToString (StringType.UTF8, offset, count));
		Assert.AreEqual (stringParameter, stringException.ParamName);
		Assert.AreEqual (expectedValue, stringException.ActualValue);
	}

	[TestMethod]
	public void RangesAcceptEmptyDataAtEnd ()
	{
		Assert.AreEqual (0, TestVector.Mid (TestVector.Count, 0).Count);
		Assert.AreEqual (string.Empty, TestVector.ToString (StringType.UTF8, TestVector.Count, 0));
		Assert.AreEqual (0, new ByteVector ().Mid (0, 0).Count);
		Assert.AreEqual (string.Empty, new ByteVector ().ToString (StringType.UTF8, 0, 0));
	}

	[TestMethod]
	public void ConstructorsValidateArguments ()
	{
		Assert.AreEqual ("data", Assert.ThrowsExactly<ArgumentNullException> (() => new ByteVector (null, 0)).ParamName);
		Assert.AreEqual ("length", Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new ByteVector (new byte[1], 2)).ParamName);
		Assert.AreEqual ("length", Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new ByteVector (new byte[1], -1)).ParamName);
		Assert.AreEqual ("size", Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new ByteVector (-1)).ParamName);
		Assert.AreEqual ("size", Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new ByteVector (-1, 1)).ParamName);
	}

	[TestMethod]
	public void ConstructorsPreserveNullAsEmpty ()
	{
		Assert.AreEqual (0, new ByteVector ((ByteVector)null).Count);
		Assert.AreEqual (0, new ByteVector ((byte[])null).Count);
	}

	[TestMethod]
	public void NullArgumentsRetainParameterNames ()
	{
		Assert.AreEqual ("pattern", Assert.ThrowsExactly<ArgumentNullException> (() => TestVector.ContainsAt (null, 0)).ParamName);
		Assert.AreEqual ("pattern", Assert.ThrowsExactly<ArgumentNullException> (() => TestVector.StartsWith (null)).ParamName);
		Assert.AreEqual ("pattern", Assert.ThrowsExactly<ArgumentNullException> (() => TestVector.EndsWith (null)).ParamName);
		Assert.AreEqual ("pattern", Assert.ThrowsExactly<ArgumentNullException> (() => TestVector.EndsWithPartialMatch (null)).ParamName);
		Assert.AreEqual ("other", Assert.ThrowsExactly<ArgumentNullException> (() => TestVector.CompareTo (null)).ParamName);
		Assert.AreEqual ("path", Assert.ThrowsExactly<ArgumentNullException> (() => ByteVector.FromPath (null)).ParamName);
		Assert.AreEqual ("abstraction", Assert.ThrowsExactly<ArgumentNullException> (() => ByteVector.FromFile (null)).ParamName);
		Assert.AreEqual ("stream", Assert.ThrowsExactly<ArgumentNullException> (() => ByteVector.FromStream (null)).ParamName);
	}

	[TestMethod]
	[DataRow ("<", true)]
	[DataRow ("<", false)]
	[DataRow ("<=", true)]
	[DataRow ("<=", false)]
	[DataRow (">", true)]
	[DataRow (">", false)]
	[DataRow (">=", true)]
	[DataRow (">=", false)]
	public void ComparisonOperatorsValidateArguments (string comparison, bool nullFirst)
	{
		ByteVector first = nullFirst ? null : TestVector;
		ByteVector second = nullFirst ? TestVector : null;
		var exception = Assert.ThrowsExactly<ArgumentNullException> (() => {
			_ = comparison switch {
				"<" => first < second,
				"<=" => first <= second,
				">" => first > second,
				">=" => first >= second,
				_ => throw new InvalidOperationException ()
			};
		});
		Assert.AreEqual (nullFirst ? "first" : "second", exception.ParamName);
	}

	[TestMethod]
	public void CopyResize ()
	{
		var a = new ByteVector (TestVector);
		var b = ByteVector.FromString ("ABCDEFGHIJKL", StringType.UTF8);
		a.Resize (12);

		Assert.AreEqual (b, a);
		Assert.AreEqual (b.ToString (), a.ToString ());
		Assert.AreNotEqual (TestVector.Count, a.Count);
	}

	[TestMethod]
	public void Int ()
	{
		Assert.AreEqual (int.MaxValue, ByteVector.FromInt (int.MaxValue).ToInt ());
		Assert.AreEqual (int.MinValue, ByteVector.FromInt (int.MinValue).ToInt ());
		Assert.AreEqual (0, ByteVector.FromInt (0).ToInt ());
		Assert.AreEqual (30292, ByteVector.FromInt (30292).ToInt ());
		Assert.AreEqual (-30292, ByteVector.FromInt (-30292).ToInt ());
		Assert.AreEqual (-1, ByteVector.FromInt (-1).ToInt ());
	}

	[TestMethod]
	public void UInt ()
	{
		Assert.AreEqual (uint.MaxValue, ByteVector.FromUInt (uint.MaxValue).ToUInt ());
		Assert.AreEqual (uint.MinValue, ByteVector.FromUInt (uint.MinValue).ToUInt ());
		Assert.AreEqual (0u, ByteVector.FromUInt (0).ToUInt ());
		Assert.AreEqual (30292u, ByteVector.FromUInt (30292).ToUInt ());
	}

	[TestMethod]
	public void Long ()
	{
		Assert.AreEqual (ulong.MaxValue, ByteVector.FromULong (ulong.MaxValue).ToULong ());
		Assert.AreEqual (ulong.MinValue, ByteVector.FromULong (ulong.MinValue).ToULong ());
		Assert.AreEqual (0u, ByteVector.FromULong (0).ToULong ());
		Assert.AreEqual (30292u, ByteVector.FromULong (30292).ToULong ());
	}

	[TestMethod]
	public void Short ()
	{
		Assert.AreEqual (ushort.MaxValue, ByteVector.FromUShort (ushort.MaxValue).ToUShort ());
		Assert.AreEqual (ushort.MinValue, ByteVector.FromUShort (ushort.MinValue).ToUShort ());
		Assert.AreEqual (0, ByteVector.FromUShort (0).ToUShort ());
		Assert.AreEqual (8009, ByteVector.FromUShort (8009).ToUShort ());
	}

	[TestMethod]
	public void FromUri ()
	{
		var vector = ByteVector.FromPath (TestPath.Samples + "vector.bin");
		Assert.AreEqual (3282169185, vector.Checksum);
		Assert.AreEqual ("1aaa46c484d70c7c80510a5f99e7805d", MD5Hash (vector.Data));
	}

	[TestMethod]
	[Ignore ("Skip performance testing")]
	public void OperatorAdd ()
	{
		using (new CodeTimer ("Operator Add")) {
			var vector = new ByteVector ();
			for (int i = 0; i < 10000; i++) {
				vector += ByteVector.FromULong (55);
			}
		}

		using (new CodeTimer ("Function Add")) {
			var vector = new ByteVector ();
			for (int i = 0; i < 10000; i++) {
				vector.Add (ByteVector.FromULong (55));
			}
		}
	}

	[TestMethod]
	public void CommentsFrameError ()
	{
		// http://bugzilla.gnome.org/show_bug.cgi?id=582735
		// Comments data found in the wild
		var vector = new ByteVector (
			1, 255, 254, 73, 0, 68, 0, 51, 0, 71, 0, 58, 0, 32, 0, 50, 0, 55, 0, 0, 0);

		var encoding = (StringType)vector[0];
		//var language = vector.ToString (StringType.Latin1, 1, 3);
		var split = vector.ToStrings (encoding, 4, 3);
		Assert.AreEqual (2, split.Length);
	}

	static string MD5Hash (byte[] bytes)
	{
		var md5 = MD5.Create ();
		byte[] hash_bytes = md5.ComputeHash (bytes);
		string hash_string = string.Empty;

		for (int i = 0; i < hash_bytes.Length; i++) {
			hash_string += Convert.ToString (hash_bytes[i], 16).PadLeft (2, '0');
		}

		return hash_string.PadLeft (32, '0');
	}
}

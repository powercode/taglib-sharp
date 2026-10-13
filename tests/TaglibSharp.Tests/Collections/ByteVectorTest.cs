using System.Security.Cryptography;

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

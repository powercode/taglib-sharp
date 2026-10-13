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
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (4095)]
	[DataRow (4096)]
	[DataRow (4097)]
	[DataRow (16384)]
	public void FromStreamReadsRemainingBytes (int length)
	{
		byte[] source = new byte[length + 13];
		new Random (42).NextBytes (source);
		using var stream = new MemoryStream (source);
		stream.Position = 13;

		var vector = ByteVector.FromStream (stream);

		Assert.AreSequenceEqual (source.AsSpan (13).ToArray (), vector.Data);
		Assert.AreEqual (stream.Length, stream.Position);
		Assert.IsTrue (stream.CanRead);
		var field = typeof (ByteVector).GetField ("data", BindingFlags.Instance | BindingFlags.NonPublic);
		var storage = (List<byte>)field.GetValue (vector);
		Assert.AreEqual (length, storage.Capacity);
	}

	[TestMethod]
	[DataRow (true, 0, 7)]
	[DataRow (true, 8193, 7)]
	[DataRow (false, 0, 7)]
	[DataRow (false, 8193, 7)]
	[DataRow (false, 4096, 4096)]
	[DataRow (false, 8192, 4096)]
	public void FromStreamHandlesShortReadsAndNonSeekableStreams (bool canSeek, int length, int maxRead)
	{
		byte[] source = new byte[length];
		new Random (42).NextBytes (source);
		using var stream = new TestReadStream (source, canSeek, maxRead);

		Assert.AreSequenceEqual (source, ByteVector.FromStream (stream).Data);
		Assert.AreEqual (1, stream.EndOfStreamReads);
		Assert.IsTrue (stream.CanRead);
	}

	[TestMethod]
	[DataRow (0L)]
	[DataRow (13L)]
	[DataRow (10000L)]
	public void FromStreamUsesLengthOnlyAsCapacityHint (long reportedLength)
	{
		byte[] source = new byte[8193];
		new Random (42).NextBytes (source);
		using var stream = new TestReadStream (source, reportedLength: reportedLength);

		Assert.AreSequenceEqual (source, ByteVector.FromStream (stream).Data);
		Assert.AreEqual (1, stream.EndOfStreamReads);
	}

	[TestMethod]
	public void FromStreamAcceptsPositionBeyondEnd ()
	{
		using var stream = new MemoryStream (new byte[3]);
		stream.Position = 10;
		Assert.AreEqual (0, ByteVector.FromStream (stream).Count);
		Assert.AreEqual (10L, stream.Position);
	}

	[TestMethod]
	[DataRow (true, true, 0, 7)]
	[DataRow (true, true, 3, 7)]
	[DataRow (true, true, 8193, 7)]
	[DataRow (true, true, 8193, 4096)]
	[DataRow (true, true, 8193, int.MaxValue)]
	[DataRow (true, false, 8193, 7)]
	[DataRow (false, true, 0, 7)]
	[DataRow (false, true, 8193, 7)]
	[DataRow (false, false, 8193, 7)]
	public void FromStreamPreservesFirstChunk (bool canSeek, bool copyFirstChunk, int length, int maxRead)
	{
		byte[] source = new byte[length];
		new Random (42).NextBytes (source);
		using var stream = new TestReadStream (source, canSeek, maxRead);
		var method = typeof (ByteVector).GetMethod ("FromStream", BindingFlags.Static | BindingFlags.NonPublic,
			new[] { typeof (Stream), typeof (byte[]).MakeByRefType (), typeof (bool) });
		object[] arguments = { stream, null, copyFirstChunk };

		var vector = (ByteVector)method.Invoke (null, arguments);

		Assert.AreSequenceEqual (source, vector.Data);
		if (copyFirstChunk) {
			byte[] expected = new byte[4096];
			source.AsSpan (0, Math.Min (4096, Math.Min (length, maxRead))).CopyTo (expected);
			Assert.AreSequenceEqual (expected, (byte[])arguments[1]);
		} else {
			Assert.IsNull (arguments[1]);
		}
	}

	[TestMethod]
	public void FromStreamRejectsOversizedStreams ()
	{
		using var stream = new TestReadStream (Array.Empty<byte> (), reportedLength: (long)Array.MaxLength + 1);
		Assert.ThrowsExactly<IOException> (() => ByteVector.FromStream (stream));
		Assert.AreEqual (0, stream.ReadCalls);
	}

	[TestMethod]
	public void FromStreamPropagatesReadErrors ()
	{
		using var stream = new TestReadStream (new byte[20], maxRead: 7) { FailAfter = 7 };
		Assert.ThrowsExactly<IOException> (() => ByteVector.FromStream (stream));
		Assert.IsTrue (stream.CanRead);
	}

	[TestMethod]
	public void FromStreamReadsKnownSizeInOneBulkRead ()
	{
		byte[] source = new byte[65536];
		new Random (42).NextBytes (source);
		using var stream = new TestReadStream (source);

		Assert.AreSequenceEqual (source, ByteVector.FromStream (stream).Data);
		Assert.AreEqual (2, stream.ReadCalls);
	}

	[TestMethod]
	public void FromStreamAllocatesOnlyFinalStorage ()
	{
		byte[] source = new byte[65536];
		using var stream = new MemoryStream (source);
		for (int i = 0; i < 100; ++i) {
			stream.Position = 0;
			ByteVector.FromStream (stream);
			_ = new ByteVector (source);
		}

		long before = GC.GetAllocatedBytesForCurrentThread ();
		var baseline = new ByteVector (source);
		long baselineAllocation = GC.GetAllocatedBytesForCurrentThread () - before;

		stream.Position = 0;
		before = GC.GetAllocatedBytesForCurrentThread ();
		var vector = ByteVector.FromStream (stream);
		long allocation = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.AreEqual (baselineAllocation, allocation);
		Assert.AreSequenceEqual (source, vector.Data);
		GC.KeepAlive (baseline);
	}

	sealed class TestReadStream : Stream
	{
		readonly MemoryStream stream;
		readonly bool canSeek;
		readonly int maxRead;
		readonly long? reportedLength;

		public TestReadStream (byte[] source, bool canSeek = true, int maxRead = int.MaxValue, long? reportedLength = null)
		{
			stream = new MemoryStream (source, false);
			this.canSeek = canSeek;
			this.maxRead = maxRead;
			this.reportedLength = reportedLength;
		}

		public override bool CanSeek => canSeek;
		public override bool CanRead => stream.CanRead;
		public override bool CanWrite => false;
		public override long Length => canSeek ? reportedLength ?? stream.Length : throw new NotSupportedException ();
		public override long Position {
			get => canSeek ? stream.Position : throw new NotSupportedException ();
			set => throw new NotSupportedException ();
		}

		public int EndOfStreamReads { get; private set; }
		public int ReadCalls { get; private set; }
		public long FailAfter { get; init; } = long.MaxValue;

		public override int Read (Span<byte> buffer)
		{
			++ReadCalls;
			if (stream.Position >= FailAfter)
				throw new IOException ("Test read failure.");

			int read = stream.Read (buffer.Slice (0, Math.Min (buffer.Length, maxRead)));
			if (read == 0 && ++EndOfStreamReads > 1)
				throw new IOException ("Reading continued after EOF.");
			return read;
		}

		public override int Read (byte[] buffer, int offset, int count)
		{
			return Read (buffer.AsSpan (offset, count));
		}

		public override void Flush () => throw new NotSupportedException ();
		public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException ();
		public override void SetLength (long value) => throw new NotSupportedException ();
		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();

		protected override void Dispose (bool disposing)
		{
			if (disposing)
				stream.Dispose ();
			base.Dispose (disposing);
		}
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
		Assert.AreSequenceEqual (new byte[] { 0xff, 0xfe }, ByteVector.FromString (null, StringType.UTF16, -1).Data);
		Assert.AreSequenceEqual (new byte[] { 0xff, 0xfe }, ByteVector.FromString ("", StringType.UTF16, -1).Data);
		Assert.AreSequenceEqual (Encoding.UTF8.GetBytes ("AB"), ByteVector.FromString ("ABC", 2).Data);
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

		Assert.AreSequenceEqual (new byte[] { 1, 2, 3 }, vector.Data);
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
		Assert.AreSequenceEqual (ByteVector.FromString (text.ToString (), type).Data, ByteVector.FromString (text, type).Data);
		Assert.AreSequenceEqual (ByteVector.FromString (text.ToString (), type, 4).Data, ByteVector.FromString (text, type, 4).Data);
		Assert.AreSequenceEqual (ByteVector.FromString ("", type).Data, ByteVector.FromString (ReadOnlySpan<char>.Empty, type).Data);
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
		Assert.AreSequenceEqual (expected, vector.Data);
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
	public void MidReturnsIndependentStorage ()
	{
		ByteVector source = "ABCDE";
		var result = source.Mid (1, 3);

		source[1] = (byte)'X';
		Assert.AreEqual (ByteVector.FromString ("BCD", StringType.UTF8), result);
		result[1] = (byte)'Y';
		Assert.AreEqual (ByteVector.FromString ("AXCDE", StringType.UTF8), source);
	}

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (65536)]
	[DataRow (1048576)]
	public void MidAllocatesOnlyFinalStorage (int length)
	{
		byte[] bytes = new byte[length + 13];
		new Random (42).NextBytes (bytes);
		var source = new ByteVector (bytes);
		for (int i = 0; i < 10; ++i)
			source.Mid (13, length);

		long before = GC.GetAllocatedBytesForCurrentThread ();
		var baseline = new ByteVector (bytes.AsSpan (13, length));
		long baselineAllocation = GC.GetAllocatedBytesForCurrentThread () - before;

		before = GC.GetAllocatedBytesForCurrentThread ();
		var result = source.Mid (13, length);
		long allocation = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.AreEqual (baselineAllocation, allocation);
		Assert.AreEqual (baseline, result);
		GC.KeepAlive (baseline);
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
	[DataRow (StringType.Latin1)]
	[DataRow (StringType.UTF8)]
	[DataRow (StringType.UTF16)]
	[DataRow (StringType.UTF16BE)]
	[DataRow (StringType.UTF16LE)]
	public void ToStringsHandlesInlineBufferAndSpillover (StringType type)
	{
		foreach (int count in new[] { 0, 1, 2, 3, 4, 5, 8, 9, 17 }) {
			string[] expected = Enumerable.Range (0, count).Select (i => $"value{i}").ToArray ();
			var vector = new ByteVector ((byte)0xff);
			if (count != 0)
				vector.Add (ByteVector.FromString (string.Join ("\0", expected), type));

			Assert.AreSequenceEqual (expected, vector.ToStrings (type, 1));
		}
	}

	[TestMethod]
	[DataRow (StringType.Latin1)]
	[DataRow (StringType.UTF8)]
	[DataRow (StringType.UTF16)]
	[DataRow (StringType.UTF16BE)]
	[DataRow (StringType.UTF16LE)]
	public void ToStringsHonorsCountAndPreservesRemainder (StringType type)
	{
		var vector = new ByteVector ((byte)0xff);
		vector.Add (ByteVector.FromString ("a\0b\0c\0d\0e\0f", type));

		Assert.AreSequenceEqual (new[] { "a\0b\0c\0d\0e\0f" }, vector.ToStrings (type, 1, 1));
		Assert.AreSequenceEqual (new[] { "a", "b", "c\0d\0e\0f" }, vector.ToStrings (type, 1, 3));
		Assert.AreSequenceEqual (new[] { "a", "b", "c", "d\0e\0f" }, vector.ToStrings (type, 1, 4));
		Assert.AreSequenceEqual (new[] { "a", "b", "c", "d", "e\0f" }, vector.ToStrings (type, 1, 5));
		Assert.AreSequenceEqual (new[] { "a", "b", "c", "d", "e", "f" }, vector.ToStrings (type, 1, 10));
		Assert.AreEqual (0, vector.ToStrings (type, 1, 0).Length);
		Assert.AreEqual (0, vector.ToStrings (type, vector.Count).Length);
	}

	[TestMethod]
	[DataRow (StringType.Latin1)]
	[DataRow (StringType.UTF8)]
	[DataRow (StringType.UTF16)]
	[DataRow (StringType.UTF16BE)]
	[DataRow (StringType.UTF16LE)]
	public void ToStringsPreservesEmptyEntriesAndIgnoresTrailingDelimiter (StringType type)
	{
		var vector = ByteVector.FromString ("\0a\0\0b\0\0c\0", type);
		Assert.AreSequenceEqual (new[] { "", "a", "", "b", "", "c" }, vector.ToStrings (type, 0));
	}

	[TestMethod]
	[DataRow (0)]
	[DataRow (1)]
	[DataRow (2)]
	[DataRow (3)]
	[DataRow (4)]
	public void ToStringsAllocatesOnlyFinalArrayForSmallEmptyResults (int count)
	{
		var vector = new ByteVector (new byte[count]);
		for (int i = 0; i < 100; ++i)
			vector.ToStrings (StringType.UTF8, 0);

		long before = GC.GetAllocatedBytesForCurrentThread ();
		var baseline = count == 0 ? Array.Empty<string> () : new string[count];
		long baselineAllocation = GC.GetAllocatedBytesForCurrentThread () - before;

		before = GC.GetAllocatedBytesForCurrentThread ();
		var result = vector.ToStrings (StringType.UTF8, 0);
		long allocation = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.AreEqual (baselineAllocation, allocation);
		Assert.AreSequenceEqual (Enumerable.Repeat (string.Empty, count).ToArray (), result);
		GC.KeepAlive (baseline);
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

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void DecodingMatchesPreviousBomAndMalformedInputBehavior (bool useBrokenLatin1)
	{
		bool previous = ByteVector.UseBrokenLatin1Behavior;
		try {
			ByteVector.UseBrokenLatin1Behavior = useBrokenLatin1;
			var random = new Random (42);
			var sources = new List<byte[]> {
				Array.Empty<byte> (),
				new byte[] { 0xff, 0xfe, 65, 0, 0xff },
				new byte[] { 0xfe, 0xff, 0, 65, 0xfe },
				new byte[] { 0xff, 0xfe, 0xfe, 0xff, 0xff, 0xfe, 65, 0 },
				new byte[] { 0xef, 0xbb, 0xbf, 0xef, 0xbf, 0xbe, 0xef, 0xbb, 0xbf, 65 },
				new byte[] { 0xff, 0xfe, 0, 0xd8, 65, 0, 0, 0xdc },
				new byte[] { 0xfe, 0xff, 0xd8, 0, 0, 65, 0xdc, 0 },
				new byte[] { 65, 0, 0, 1, 0, 0, 66, 0, 0, 0, 0 },
			};
			for (int i = 0; i < 64; ++i) {
				var bytes = new byte[random.Next (1, 33)];
				random.NextBytes (bytes);
				sources.Add (bytes);
			}

			foreach (StringType type in new[] { StringType.Latin1, StringType.UTF8, StringType.UTF16, StringType.UTF16BE, StringType.UTF16LE, (StringType)255 }) {
				Encoding lastEncoding = Encoding.Unicode;
				_ = new ByteVector (0xff, 0xfe).ToString (StringType.UTF16);
				foreach (byte[] bytes in sources) {
					var vector = new ByteVector (bytes);
					for (int offset = 0; offset <= bytes.Length; ++offset) {
						for (int count = 0; count <= bytes.Length - offset; ++count) {
							string expected = DecodeReference (bytes, type, offset, count, ref lastEncoding);
							Assert.AreEqual (expected, vector.ToString (type, offset, count),
								$"Encoding={type}, offset={offset}, count={count}, bytes={Convert.ToHexString (bytes)}");
						}
						foreach (int limit in new[] { 0, 1, 2, 4, int.MaxValue }) {
							Encoding initialEncoding = lastEncoding;
							var expected = new List<string> ();
							int position = offset;
							int alignment = ByteVector.TextDelimiter (type).Count;
							while (expected.Count < limit && position < bytes.Length) {
								int start = position;
								position = expected.Count + 1 == limit ? bytes.Length
									: vector.Find (ByteVector.TextDelimiter (type), start, alignment);
								if (position < 0)
									position = bytes.Length;
								string value = position == start ? "" : DecodeReference (bytes, type, start, position - start, ref lastEncoding);
								if (value.Length != 0 && (value[0] == '\ufeff' || value[0] == '\ufffe'))
									value = value[1..];
								expected.Add (value);
								position += alignment;
							}
							_ = (initialEncoding == Encoding.Unicode ? new ByteVector (0xff, 0xfe)
								: new ByteVector (0xfe, 0xff)).ToString (StringType.UTF16);
							Assert.AreSequenceEqual (expected.ToArray (), vector.ToStrings (type, offset, limit),
								$"Encoding={type}, offset={offset}, limit={limit}, bytes={Convert.ToHexString (bytes)}");
						}
					}
				}
			}
		} finally {
			ByteVector.UseBrokenLatin1Behavior = previous;
		}
	}

	static string DecodeReference (byte[] bytes, StringType type, int offset, int count, ref Encoding lastEncoding)
	{
		if (type == StringType.UTF16 && bytes.Length - offset >= 2) {
			if (bytes[offset] == 0xff && bytes[offset + 1] == 0xfe)
				lastEncoding = Encoding.Unicode;
			else if (bytes[offset] == 0xfe && bytes[offset + 1] == 0xff)
				lastEncoding = Encoding.BigEndianUnicode;
		}
		Encoding encoding = type switch {
			StringType.UTF16 => lastEncoding,
			StringType.UTF16BE => Encoding.BigEndianUnicode,
			StringType.UTF16LE => Encoding.Unicode,
			StringType.UTF8 => Encoding.UTF8,
			_ => ByteVector.UseBrokenLatin1Behavior ? Encoding.Default : Encoding.Latin1,
		};
		string value = encoding.GetString (bytes.AsSpan (offset, count));
		return value.Length != 0 && (value[0] == '\ufeff' || value[0] == '\ufffe') ? value[1..] : value;
	}

	[TestMethod]
	[DataRow (StringType.UTF8)]
	[DataRow (StringType.UTF16)]
	[DataRow (StringType.UTF16BE)]
	[DataRow (StringType.UTF16LE)]
	public void ToStringWithBomAllocatesOnlyFinalString (StringType type)
	{
		string text = new string ('A', 4096);
		var vector = ByteVector.FromString (type == StringType.UTF16 ? text : "\ufeff" + text, type);
		Encoding encoding = type == StringType.UTF8 ? Encoding.UTF8
			: type == StringType.UTF16BE ? Encoding.BigEndianUnicode : Encoding.Unicode;
		byte[] bytes = encoding.GetBytes (text);
		for (int i = 0; i < 100; ++i) {
			encoding.GetString (bytes.AsSpan ());
			vector.ToString (type);
		}

		long before = GC.GetAllocatedBytesForCurrentThread ();
		string baseline = encoding.GetString (bytes.AsSpan ());
		long baselineAllocation = GC.GetAllocatedBytesForCurrentThread () - before;

		before = GC.GetAllocatedBytesForCurrentThread ();
		string result = vector.ToString (type);
		long allocation = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.AreEqual (baselineAllocation, allocation);
		Assert.AreEqual (text, result);
		GC.KeepAlive (baseline);
	}

	[TestMethod]
	public void ToStringsWithRepeatedBomsAllocatesOnlyFinalStringsAndArray ()
	{
		var vector = ByteVector.FromString ("\ufeffA\0\ufeffB", StringType.UTF16);
		var withoutBoms = ByteVector.FromString ("A\0B", StringType.UTF16LE);
		for (int i = 0; i < 100; ++i) {
			vector.ToStrings (StringType.UTF16, 0);
			withoutBoms.ToStrings (StringType.UTF16LE, 0);
		}

		long before = GC.GetAllocatedBytesForCurrentThread ();
		var baseline = withoutBoms.ToStrings (StringType.UTF16LE, 0);
		long baselineAllocation = GC.GetAllocatedBytesForCurrentThread () - before;

		before = GC.GetAllocatedBytesForCurrentThread ();
		var result = vector.ToStrings (StringType.UTF16, 0);
		long allocation = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.AreEqual (baselineAllocation, allocation);
		Assert.AreSequenceEqual (new[] { "A", "B" }, result);
		GC.KeepAlive (baseline);
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

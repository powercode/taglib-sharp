using File = TagLib.File;

namespace TaglibSharp.Tests.Collections
{
	[TestClass]
	public class FileTest
	{
		static readonly string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
		static readonly string Pattern1 = "efg";
		static readonly string Pattern3 = "bbbbba";

		// length1 is smaller than the used buffer size
		// length2 is bigger than the used buffer size
		// length3 is even more bigger than length2 and used to catch some special cases
		static readonly int length1 = (int)(0.75 * File.BufferSize);
		static readonly int length2 = (int)(1.5 * File.BufferSize);
		static readonly int length3 = (int)(3.1 * File.BufferSize);

		File CreateFile (int length)
		{
			byte[] data = new byte[length];

			for (int i = 0; i < length; i++) {
				data[i] = (byte)Chars[i % Chars.Length];
			}

			return new TestFile (new MemoryFileAbstraction (data.Length, data));
		}

		[TestMethod]
		[DataRow (0, 10, 7)]
		[DataRow (1, 10, 7)]
		[DataRow (10, 10, 7)]
		[DataRow (20, 10, 7)]
		[DataRow (20, 0, 7)]
		[DataRow (4096, 8193, 4096)]
		[DataRow (8193, 8193, 7)]
		[DataRow (10000, 8193, 4096)]
		public void ReadBlockHandlesShortReadsAndEndOfStream (int requested, int available, int maxRead)
		{
			byte[] source = new byte[available + 13];
			new Random (42).NextBytes (source);
			using var stream = new BlockReadStream (source, maxRead);
			stream.Position = 13;
			using var file = new TestFile (new StreamAbstraction (stream));

			var result = file.ReadBlock (requested);

			int expectedLength = Math.Min (requested, available);
			Assert.AreSequenceEqual (source.AsSpan (13, expectedLength).ToArray (), result.Data);
			Assert.AreEqual (13L + expectedLength, stream.Position);
			Assert.AreEqual (requested > available ? 1 : 0, stream.EndOfStreamReads);
			Assert.IsTrue (stream.CanRead);
			if (requested == 0) {
				Assert.AreEqual (0, stream.ReadCalls);
				Assert.AreEqual (File.AccessMode.Closed, file.Mode);
			}
		}

		[TestMethod]
		public void ReadBlockRejectsNegativeLengthWithoutOpeningStream ()
		{
			using var file = CreateFile (10);
			var exception = Assert.ThrowsExactly<ArgumentException> (() => file.ReadBlock (-1));
			Assert.AreEqual ("length", exception.ParamName);
			Assert.AreEqual (File.AccessMode.Closed, file.Mode);
		}

		[TestMethod]
		public void ReadBlockPropagatesReadErrors ()
		{
			using var stream = new BlockReadStream (new byte[20], 7) { FailAfter = 7 };
			using var file = new TestFile (new StreamAbstraction (stream));
			Assert.ThrowsExactly<IOException> (() => file.ReadBlock (20));
			Assert.AreEqual (7L, stream.Position);
			Assert.IsTrue (stream.CanRead);
		}

		[TestMethod]
		[DataRow (0)]
		[DataRow (100)]
		[DataRow (65536)]
		public void ReadBlockAllocatesOnlyBackingStorage (int available)
		{
			byte[] source = new byte[65536];
			using var stream = new MemoryStream (source, 0, available, false);
			using var file = new TestFile (new StreamAbstraction (stream));
			for (int i = 0; i < 100; ++i) {
				stream.Position = 0;
				file.ReadBlock (source.Length);
			}

			long before = GC.GetAllocatedBytesForCurrentThread ();
			var baseline = new ByteVector (source.AsSpan ());
			long baselineAllocation = GC.GetAllocatedBytesForCurrentThread () - before;

			stream.Position = 0;
			before = GC.GetAllocatedBytesForCurrentThread ();
			var result = file.ReadBlock (source.Length);
			long allocation = GC.GetAllocatedBytesForCurrentThread () - before;

			Assert.AreEqual (baselineAllocation, allocation);
			Assert.AreEqual (available, result.Count);
			GC.KeepAlive (baseline);
		}

		sealed class StreamAbstraction (Stream stream) : File.IFileAbstraction
		{
			public string Name => "MEMORY";
			public Stream ReadStream => stream;
			public Stream WriteStream => stream;
			public void CloseStream (Stream stream) => stream.Dispose ();
		}

		sealed class BlockReadStream (byte[] source, int maxRead) : MemoryStream (source, false)
		{
			public int ReadCalls { get; private set; }
			public int EndOfStreamReads { get; private set; }
			public long FailAfter { get; init; } = long.MaxValue;

			public override int Read (Span<byte> buffer)
			{
				++ReadCalls;
				if (Position >= FailAfter)
					throw new IOException ("Test read failure.");
				int read = base.Read (buffer[..Math.Min (buffer.Length, maxRead)]);
				if (read == 0 && ++EndOfStreamReads > 1)
					throw new IOException ("Reading continued after EOF.");
				return read;
			}
		}

		[TestMethod]
		public void RFind ()
		{
			// file1
			var file1 = CreateFile (length1);
			Assert.AreEqual (-1, file1.RFind (Pattern1));
			Assert.AreEqual (-1, file1.RFind (Pattern1, 5));

			file1.Insert (Pattern1, 30, 2);
			Assert.AreEqual (30, file1.RFind (Pattern1));

			file1.Insert (Pattern1, length1 / 2, 2);
			Assert.AreEqual (length1 / 2, file1.RFind (Pattern1));

			file1.Insert (Pattern1, length1 - 30, 2);
			Assert.AreEqual (length1 - 30, file1.RFind (Pattern1));

			Assert.AreEqual (30, file1.RFind (Pattern1, length1 - length1 / 2 + 1));
			Assert.AreEqual (length1 / 2, file1.RFind (Pattern1, 30 + 1));
			Assert.AreEqual (length1 - 30, file1.RFind (Pattern1, 2 + 1));


			// file2
			var file2 = CreateFile (length2);
			Assert.AreEqual (-1, file2.RFind (Pattern1));
			Assert.AreEqual (-1, file2.RFind (Pattern1, 8));

			file2.Insert (Pattern1, 30, Pattern1.Length);
			Assert.AreEqual (30, file2.RFind (Pattern1));

			file2.Insert (Pattern1, length2 / 2, Pattern1.Length);
			Assert.AreEqual (length2 / 2, file2.RFind (Pattern1));

			file2.Insert (Pattern1, length2 - 30, Pattern1.Length);
			Assert.AreEqual (length2 - 30, file2.RFind (Pattern1));

			Assert.AreEqual (30, file2.RFind (Pattern1, length2 - length2 / 2));
			Assert.AreEqual (length2 / 2, file2.RFind (Pattern1, 30));
			Assert.AreEqual (length2 - 30, file2.RFind (Pattern1, 2));


			// file3
			// especially used to test searching if the search pattern is splattened to
			// different buffer reads
			// this test is specialized to the used algorithm
			var file3 = CreateFile (length3);
			Assert.AreEqual (-1, file3.RFind (Pattern1));
			Assert.AreEqual (-1, file3.RFind (Pattern1, 13));

			long buffer_cross2 = file3.Length - 2 * File.BufferSize - (Pattern1.Length / 2);
			file3.Insert (Pattern1, buffer_cross2, Pattern1.Length);
			Assert.AreEqual (buffer_cross2, file3.RFind (Pattern1));

			long buffer_cross1 = file3.Length - File.BufferSize - (Pattern1.Length / 2);
			file3.Insert (Pattern1, buffer_cross1, Pattern1.Length);
			Assert.AreEqual (buffer_cross1, file3.RFind (Pattern1));

			// see Find()
			long buffer_cross3 = file3.Length - File.BufferSize - Pattern3.Length + 1;
			file3.Insert (Pattern3, buffer_cross3 + 1, Pattern3.Length);
			file3.Insert (Pattern3, buffer_cross3, Pattern3.Length);
			Assert.AreEqual (buffer_cross3, file3.RFind (Pattern3));
		}

		[TestMethod]
		public void Find ()
		{
			// file1
			var file1 = CreateFile (length1);
			Assert.AreEqual (Chars.IndexOf ('U'), file1.Find ((byte)'U'));

			Assert.AreEqual (-1, file1.Find (Pattern1));
			Assert.AreEqual (-1, file1.Find (Pattern1, 9));

			file1.Insert (Pattern1, length1 - 30, Pattern1.Length);
			Assert.AreEqual (length1 - 30, file1.Find (Pattern1));

			file1.Insert (Pattern1, length1 / 2, Pattern1.Length);
			Assert.AreEqual (length1 / 2, file1.Find (Pattern1));

			file1.Insert (Pattern1, 30, Pattern1.Length);
			Assert.AreEqual (30, file1.Find (Pattern1));


			// file2
			var file2 = CreateFile (length2);
			Assert.AreEqual (Chars.IndexOf ('M'), file2.Find ((byte)'M'));

			Assert.AreEqual (-1, file2.Find (Pattern1));
			Assert.AreEqual (-1, file2.Find (Pattern1, 3));

			file2.Insert (Pattern1, length2 - 30, Pattern1.Length);
			Assert.AreEqual (length2 - 30, file2.Find (Pattern1));

			file2.Insert (Pattern1, length2 / 2, Pattern1.Length);
			Assert.AreEqual (length2 / 2, file2.Find (Pattern1));

			file2.Insert (Pattern1, 30, Pattern1.Length);
			Assert.AreEqual (30, file2.Find (Pattern1));

			Assert.AreEqual (30, file2.Find (Pattern1, 2));
			Assert.AreEqual (length2 / 2, file2.Find (Pattern1, 31));
			Assert.AreEqual (length2 - 30, file2.Find (Pattern1, length2 / 2 + 3));

			// file3
			// especially used to test searching if the search pattern is splattened to
			// different buffer reads.
			// this test is specialized to the used algorithm
			var file3 = CreateFile (length3);
			Assert.AreEqual (-1, file3.Find (Pattern1));
			Assert.AreEqual (-1, file3.Find (Pattern1, 13));

			long buffer_cross2 = 2 * File.BufferSize - (Pattern1.Length / 2);
			file3.Insert (Pattern1, buffer_cross2, Pattern1.Length);
			Assert.AreEqual (buffer_cross2, file3.Find (Pattern1));

			long buffer_cross1 = File.BufferSize - (Pattern1.Length / 2);
			file3.Insert (Pattern1, buffer_cross1, Pattern1.Length);
			Assert.AreEqual (buffer_cross1, file3.Find (Pattern1));

			Assert.AreEqual (buffer_cross2, file3.Find (Pattern1, buffer_cross1 + 1));

			long buffer_cross3 = File.BufferSize - 1;
			file3.Insert (Pattern3, buffer_cross3 - 1, Pattern3.Length);
			file3.Insert (Pattern3, buffer_cross3, Pattern3.Length);
			Assert.AreEqual (buffer_cross3, file3.Find (Pattern3));
		}
	}


	// Hack, to create objects of the abstarct class File to test some methods
	public class TestFile : File
	{

		public TestFile (IFileAbstraction abstraction) : base (abstraction) { }

		public override Tag GetTag (TagTypes type, bool create)
		{
			throw new System.NotImplementedException ();
		}

		public override Properties Properties => throw new System.NotImplementedException ();

		public override void RemoveTags (TagTypes types)
		{
			throw new System.NotImplementedException ();
		}

		public override void Save ()
		{
			throw new System.NotImplementedException ();
		}

		public override Tag Tag => throw new System.NotImplementedException ();
	}
}

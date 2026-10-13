using TagLib.Id3v2;

using Tag = TagLib.Id3v2.Tag;

namespace TaglibSharp.Tests.TaggingFormats;

[TestClass]
public class Id3V2LazyTest
{
	public static IEnumerable<object[]> TagCases {
		get {
			foreach (byte version in new byte[] { 2, 3, 4 })
				foreach (bool unsynchronised in new[] { false, true })
					foreach (int extendedSize in version == 2 ? new[] { 0 } : new[] { 0, version == 3 ? 10 : 6, 256 })
						yield return new object[] { version, unsynchronised, extendedSize };
		}
	}

	[TestMethod]
	[DynamicData (nameof (TagCases))]
	public void LazyReadingUsesBoundedStorage (byte version, bool unsynchronised, int extendedSize)
	{
		var original = CreateTag (version, 1048576);
		byte[] bytes = RenderTag (original, unsynchronised, extendedSize);
		var abstraction = new MemoryFileAbstraction (bytes.Length, bytes);
		using var storage = abstraction.ReadStream;
		for (int i = 0; i < 10; ++i) {
			using var warmup = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.PictureLazy);
			_ = warmup.Tag.Title;
			_ = warmup.Tag.Album;
		}

		long before = GC.GetAllocatedBytesForCurrentThread ();
		using var file = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.PictureLazy);
		string title = file.Tag.Title;
		string album = file.Tag.Album;
		long allocation = GC.GetAllocatedBytesForCurrentThread () - before;

		Assert.IsLessThan (65536L, allocation,
			$"Reading metadata with two 1 MiB attachments allocated {allocation} bytes.");
		Assert.AreEqual (original.Title, title);
		Assert.AreEqual (original.Album, album);
		var tag = (Tag)file.GetTag (TagTypes.Id3v2);
		var frames = tag.GetFrames<AttachmentFrame> ().ToArray ();
		Assert.HasCount (2, frames);
		Assert.IsTrue (frames.All (frame => !frame.IsLoaded));

		file.Dispose ();
		var expected = original.GetFrames<AttachmentFrame> ().ToArray ();
		for (int i = 0; i < frames.Length; ++i) {
			Assert.AreEqual (expected[i].Data, frames[i].Data);
			Assert.AreEqual (expected[i].MimeType, frames[i].MimeType);
			Assert.AreEqual (expected[i].Description, frames[i].Description);
			Assert.AreEqual (expected[i].Type, frames[i].Type);
			Assert.IsTrue (frames[i].IsLoaded);
		}
	}

	[TestMethod]
	[DynamicData (nameof (TagCases))]
	public void SavingUnloadedAttachmentsPreservesTheirContent (byte version, bool unsynchronised, int extendedSize)
	{
		var original = CreateTag (version, 32);
		byte[] bytes = RenderTag (original, unsynchronised, extendedSize);
		var abstraction = new MemoryFileAbstraction (bytes.Length, bytes);
		using var storage = abstraction.ReadStream;
		using (var file = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.PictureLazy)) {
			var tag = (Tag)file.GetTag (TagTypes.Id3v2);
			Assert.IsTrue (tag.GetFrames<AttachmentFrame> ().All (frame => !frame.IsLoaded));
			file.Tag.Title = "updated title";
			file.Save ();
		}

		using var saved = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.None);
		Assert.AreEqual ("updated title", saved.Tag.Title);
		Assert.AreEqual (original.Album, saved.Tag.Album);
		var savedTag = (Tag)saved.GetTag (TagTypes.Id3v2);
		var expected = original.GetFrames<AttachmentFrame> ().ToArray ();
		var actual = savedTag.GetFrames<AttachmentFrame> ().ToArray ();
		Assert.HasCount (expected.Length, actual);
		for (int i = 0; i < actual.Length; ++i) {
			Assert.AreEqual (expected[i].Data, actual[i].Data);
			Assert.AreEqual (expected[i].Description, actual[i].Description);
			Assert.AreEqual (expected[i].Type, actual[i].Type);
		}
	}

	[TestMethod]
	[DynamicData (nameof (TagCases))]
	public void EagerReadingStillLoadsAttachments (byte version, bool unsynchronised, int extendedSize)
	{
		var original = CreateTag (version, 32);
		byte[] bytes = RenderTag (original, unsynchronised, extendedSize);
		var abstraction = new MemoryFileAbstraction (bytes.Length, bytes);
		using var storage = abstraction.ReadStream;
		using var file = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.None);
		Assert.AreEqual (original.Title, file.Tag.Title);
		Assert.AreEqual (original.Album, file.Tag.Album);
		var tag = (Tag)file.GetTag (TagTypes.Id3v2);
		var actual = tag.GetFrames<AttachmentFrame> ().ToArray ();
		var expected = original.GetFrames<AttachmentFrame> ().ToArray ();
		Assert.HasCount (expected.Length, actual);
		for (int i = 0; i < actual.Length; ++i) {
			Assert.IsTrue (actual[i].IsLoaded);
			Assert.AreEqual (expected[i].Data, actual[i].Data);
		}
	}

	[TestMethod]
	[DataRow (0u)]
	[DataRow (3u)]
	[DataRow (1000u)]
	[DataRow (uint.MaxValue)]
	public void InvalidExtendedHeaderSizeIsRejected (uint size)
	{
		var body = ByteVector.FromUInt (size);
		var header = new Header {
			MajorVersion = 3,
			Flags = HeaderFlags.ExtendedHeader,
			TagSize = (uint)body.Count
		};
		var bytes = (header.Render () + body).Data;
		var abstraction = new MemoryFileAbstraction (bytes.Length, bytes);
		using var storage = abstraction.ReadStream;
		Assert.ThrowsExactly<CorruptFileException> (() => {
			using var file = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.PictureLazy);
		});
	}

	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public void OversizedFrameIsRejectedWithoutAllocatingItsPayload (bool unsynchronised)
	{
		var body = new ByteVector ("APIC") {
			ByteVector.FromUInt (int.MaxValue),
			new ByteVector (2),
			new ByteVector (5)
		};
		var header = new Header {
			MajorVersion = 3,
			Flags = unsynchronised ? HeaderFlags.Unsynchronisation : HeaderFlags.None,
			TagSize = (uint)body.Count
		};
		byte[] bytes = (header.Render () + body).Data;
		var abstraction = new MemoryFileAbstraction (bytes.Length, bytes);
		using var storage = abstraction.ReadStream;
		Assert.ThrowsExactly<CorruptFileException> (() => {
			using var file = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.PictureLazy);
		});
	}

	[TestMethod]
	[DataRow (1)]
	[DataRow (7)]
	[DataRow (4096)]
	public void UnsynchronisedReadsHandleShortReadsAndNonzeroTagOffsets (int maxRead)
	{
		var original = CreateTag (3, 8192);
		byte[] tagBytes = RenderTag (original, true, 10);
		var bytes = new byte[tagBytes.Length + 17];
		tagBytes.CopyTo (bytes, 17);
		var abstraction = new ChunkedFileAbstraction (bytes, maxRead);
		using var file = new Collections.TestFile (abstraction);
		var tag = new Tag (file, 17, ReadStyle.PictureLazy);

		Assert.AreEqual (original.Title, tag.Title);
		Assert.AreEqual (original.Album, tag.Album);
		var actual = tag.GetFrames<AttachmentFrame> ().ToArray ();
		var expected = original.GetFrames<AttachmentFrame> ().ToArray ();
		Assert.HasCount (expected.Length, actual);
		for (int i = 0; i < actual.Length; ++i) {
			Assert.IsFalse (actual[i].IsLoaded);
			Assert.AreEqual (expected[i].Data, actual[i].Data);
		}
	}

	[TestMethod]
	[DataRow (4095)]
	[DataRow (8191)]
	public void UnsynchronisationPairsCanStraddlePooledBufferBoundaries (int boundary)
	{
		var payload = new ByteVector (12288, 42);
		var attachment = new AttachmentFrame {
			MimeType = "image/png",
			TextEncoding = StringType.Latin1,
			Data = payload
		};
		int prefixLength = attachment.Render (3).Count - payload.Count;
		payload[boundary - prefixLength] = 0xff;
		payload[boundary - prefixLength + 1] = 0;
		var original = new Tag { Version = 3 };
		original.AddFrame (attachment);
		original.Album = "after attachment";
		byte[] bytes = RenderTag (original, true, 0);
		var abstraction = new MemoryFileAbstraction (bytes.Length, bytes);
		using var storage = abstraction.ReadStream;
		using var file = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.PictureLazy);

		Assert.AreEqual (original.Album, file.Tag.Album);
		var tag = (Tag)file.GetTag (TagTypes.Id3v2);
		var frame = tag.GetFrames<AttachmentFrame> ().Single ();
		Assert.IsFalse (frame.IsLoaded);
		Assert.AreEqual (payload, frame.Data);
	}

	[TestMethod]
	[DataRow ((byte)2)]
	[DataRow ((byte)3)]
	public void FrameHeadersCanContainUnsynchronisationBytes (byte version)
	{
		var attachment = new AttachmentFrame {
			MimeType = "image/png",
			TextEncoding = StringType.Latin1,
			Data = new ByteVector (32, 42)
		};
		int metadataLength = attachment.Render (version).Count - (int)FrameHeader.Size (version) - 32;
		attachment.Data = new ByteVector (255 - metadataLength, 42);
		var original = new Tag { Version = version };
		original.AddFrame (attachment);
		original.Album = "after attachment";
		byte[] bytes = RenderTag (original, true, 0);
		var abstraction = new MemoryFileAbstraction (bytes.Length, bytes);
		using var storage = abstraction.ReadStream;
		using var file = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.PictureLazy);

		Assert.AreEqual (original.Album, file.Tag.Album);
		var frame = ((Tag)file.GetTag (TagTypes.Id3v2)).GetFrames<AttachmentFrame> ().Single ();
		Assert.IsFalse (frame.IsLoaded);
		Assert.AreEqual (attachment.Data, frame.Data);
	}

	[TestMethod]
	public void UnsynchronisedReadErrorsPropagateAndCloseTheStream ()
	{
		byte[] bytes = RenderTag (CreateTag (3, 8192), true, 10);
		var abstraction = new ChunkedFileAbstraction (bytes, 7) { FailAt = 4096 };
		Assert.ThrowsExactly<IOException> (() => {
			using var file = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.PictureLazy);
		});
		Assert.AreEqual (1, abstraction.ClosedStreams);

		abstraction.FailAt = long.MaxValue;
		using var retry = new TagLib.Mpeg.AudioFile (abstraction, ReadStyle.PictureLazy);
		Assert.AreEqual ("album after attachments", retry.Tag.Album);
		Assert.IsTrue (((Tag)retry.GetTag (TagTypes.Id3v2)).GetFrames<AttachmentFrame> ()
			.All (frame => !frame.IsLoaded));
	}

	sealed class ChunkedFileAbstraction (byte[] data, int maxRead) : TagLib.File.IFileAbstraction
	{
		public long FailAt { get; set; } = long.MaxValue;
		public int ClosedStreams { get; private set; }
		public string Name => "memory.mp3";
		public Stream ReadStream => new ChunkedStream (data, maxRead, FailAt);
		public Stream WriteStream => throw new NotSupportedException ();

		public void CloseStream (Stream stream)
		{
			++ClosedStreams;
			stream.Dispose ();
		}
	}

	sealed class ChunkedStream (byte[] data, int maxRead, long failAt) : MemoryStream (data, false)
	{
		public override int Read (Span<byte> buffer)
		{
			if (Position >= failAt)
				throw new IOException ("Test read failure.");
			int length = (int)Math.Min (Math.Min (buffer.Length, maxRead), failAt - Position);
			return base.Read (buffer.Slice (0, length));
		}
	}

	static Tag CreateTag (byte version, int payloadSize)
	{
		var tag = new Tag { Version = version, Title = "title before attachments" };
		var payload = new ByteVector (payloadSize, 42);
		for (int i = 0; i + 5 < payload.Count; i += 4096) {
			payload[i] = 0xff;
			payload[i + 1] = 0;
			payload[i + 2] = 0xff;
			payload[i + 3] = 0xe0;
			payload[i + 4] = 0xff;
			payload[i + 5] = 0xff;
		}
		payload[payload.Count - 1] = 0xff;
		foreach (var type in new[] { PictureType.FrontCover, PictureType.NotAPicture })
			tag.AddFrame (new AttachmentFrame {
				Type = type,
				MimeType = "image/png",
				Filename = "cover.png",
				Description = "cover",
				TextEncoding = StringType.Latin1,
				Data = new ByteVector (payload)
			});
		tag.Album = "album after attachments";
		return tag;
	}

	static byte[] RenderTag (Tag tag, bool unsynchronised, int extendedSize)
	{
		var body = new ByteVector ();
		if (extendedSize != 0) {
			body.Add (tag.Version == 3
				? ByteVector.FromUInt ((uint)extendedSize - 4)
				: SynchData.FromUInt ((uint)extendedSize));
			body.Add (new ByteVector (extendedSize - 4));
			if (tag.Version == 4)
				body[4] = 1;
		}

		foreach (var frame in tag.GetFrames ()) {
			if (unsynchronised && tag.Version == 4)
				frame.Flags |= FrameFlags.Unsynchronisation;
			body.Add (frame.Render (tag.Version));
		}
		if (unsynchronised && tag.Version < 4)
			SynchData.UnsynchByteVector (body);
		var header = new Header {
			MajorVersion = tag.Version,
			Flags = (unsynchronised ? HeaderFlags.Unsynchronisation : HeaderFlags.None)
				| (extendedSize != 0 ? HeaderFlags.ExtendedHeader : HeaderFlags.None),
			TagSize = (uint)body.Count
		};
		return (header.Render () + body).Data;
	}
}

using System.Buffers;
using System.IO;

namespace TagLib.Id3v2
{
	/// <summary>
	///    Reads a bounded tag body, tracking physical offsets while optionally
	///    removing tag-wide unsynchronisation bytes.
	/// </summary>
	internal sealed class TagDataStream : Stream
	{
		readonly File file;
		readonly long end;
		readonly byte[] buffer;
		int bufferOffset;
		int bufferCount;
		long logicalPosition;
		bool disposed;

		public TagDataStream (File file, long position, uint length, bool unsynchronised)
		{
			this.file = file;
			PhysicalPosition = position;
			end = Math.Min (position + length, file.Length);
			if (unsynchronised)
				buffer = ArrayPool<byte>.Shared.Rent (4096);
		}

		public long PhysicalPosition { get; private set; }
		public long Remaining => end - PhysicalPosition;
		public override bool CanRead => !disposed;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException ();
		public override long Position {
			get => logicalPosition;
			set => throw new NotSupportedException ();
		}

		public ByteVector ReadBlock (int length)
		{
			return ByteVector.FromStream (this, length);
		}

		public void SkipTo (int position)
		{
			if (position < logicalPosition)
				throw new CorruptFileException ("Frame overlaps previously read tag data.");

			Skip (position - (int)logicalPosition);
		}

		public void Skip (int length)
		{
			ObjectDisposedException.ThrowIf (disposed, this);
			if (length < 0 || length > Remaining)
				throw new CorruptFileException ("Frame extends beyond the tag data.");

			if (buffer == null) {
				PhysicalPosition += length;
				logicalPosition += length;
				return;
			}

			for (int i = 0; i < length; ++i) {
				if (Remaining == 0)
					throw new CorruptFileException ("Frame extends beyond the tag data.");
				ReadDecodedByte ();
			}
			logicalPosition += length;
		}

		public override int Read (Span<byte> destination)
		{
			ObjectDisposedException.ThrowIf (disposed, this);
			if (buffer == null) {
				int length = (int)Math.Min (destination.Length, Remaining);
				if (length == 0)
					return 0;
				file.Seek (PhysicalPosition);
				int read = file.ReadBlock (destination.Slice (0, length));
				PhysicalPosition += read;
				logicalPosition += read;
				return read;
			}

			int count = 0;
			while (count < destination.Length && Remaining > 0)
				destination[count++] = ReadDecodedByte ();
			logicalPosition += count;
			return count;
		}

		byte ReadDecodedByte ()
		{
			byte value = PeekByte ();
			++bufferOffset;
			++PhysicalPosition;
			// Consume the inserted zero with its FF, even across frame or buffer boundaries.
			if (value == 0xff && Remaining > 0 && PeekByte () == 0) {
				++bufferOffset;
				++PhysicalPosition;
			}
			return value;
		}

		byte PeekByte ()
		{
			if (bufferOffset == bufferCount) {
				file.Seek (PhysicalPosition);
				bufferCount = file.ReadBlock (buffer.AsSpan (0, (int)Math.Min (4096, Remaining)));
				bufferOffset = 0;
				if (bufferCount == 0)
					throw new CorruptFileException ("Unexpected end of tag data.");
			}
			return buffer[bufferOffset];
		}

		public override int Read (byte[] buffer, int offset, int count)
		{
			ArgumentNullException.ThrowIfNull (buffer);
			return Read (buffer.AsSpan (offset, count));
		}

		protected override void Dispose (bool disposing)
		{
			if (disposing && !disposed) {
				if (buffer != null)
					ArrayPool<byte>.Shared.Return (buffer);
				disposed = true;
			}
			base.Dispose (disposing);
		}

		public override void Flush () => throw new NotSupportedException ();
		public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException ();
		public override void SetLength (long value) => throw new NotSupportedException ();
		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
	}
}

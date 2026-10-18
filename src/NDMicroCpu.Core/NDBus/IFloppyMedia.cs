//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

namespace NDMicroCpu.Core.NDBus
{
    /// <summary>
    /// Backing store for one or more floppy units behind an <see cref="NdFloppyDma"/>
    /// controller. Kept deliberately small (block read/write + attach/geometry) so the
    /// same interface serves a local file, an uploaded byte[] (web), or an HttpClient-
    /// fetched image - the controller only ever asks for byte ranges, never for files.
    /// Positions are BYTE offsets into the image; the controller computes them from the
    /// logical sector address and the sector size.
    /// </summary>
    public interface IFloppyMedia
    {
        /// <summary>Is a diskette present in this unit?</summary>
        bool IsAttached(int unit);

        /// <summary>Is the diskette in this unit write-protected?</summary>
        bool IsReadOnly(int unit);

        /// <summary>Image length in bytes for this unit (used for media-format detection).</summary>
        long Length(int unit);

        /// <summary>Read <paramref name="byteCount"/> bytes at <paramref name="position"/>;
        /// returns null on a read error (short/unreadable), which the controller maps to a
        /// CRC error.</summary>
        byte[]? ReadBlock(int unit, long position, int byteCount);

        /// <summary>Write bytes at <paramref name="position"/> (ignored for read-only media).</summary>
        void WriteBlock(int unit, long position, byte[] data);
    }

    /// <summary>
    /// A single-unit <see cref="IFloppyMedia"/> over an in-memory image byte[]. This is
    /// the web-friendly, non-blocking backing: the image bytes are preloaded (embedded,
    /// uploaded, or fetched) and the controller reads/writes ranges synchronously during
    /// its DMA. Reads past the end zero-fill (short last sector) rather than failing.
    /// </summary>
    public sealed class ByteArrayFloppyMedia : IFloppyMedia
    {
        private readonly byte[] _image;
        private readonly bool _readOnly;

        /// <summary>
        /// Wraps a diskette image held in memory as the media of drive 0.
        /// </summary>
        /// <param name="image">
        /// The image bytes.
        /// </param>
        /// <param name="readOnly">
        /// True refuses writes.
        /// </param>
        public ByteArrayFloppyMedia(byte[] image, bool readOnly = false)
        {
            _image = image;
            _readOnly = readOnly;
        }

        /// <summary>
        /// Whether a drive holds media; only drive 0 does.
        /// </summary>
        /// <param name="unit">
        /// The drive number.
        /// </param>
        /// <returns>
        /// True for drive 0 with an image.
        /// </returns>
        public bool IsAttached(int unit) => unit == 0 && _image != null;

        /// <summary>
        /// Whether the media refuses writes.
        /// </summary>
        /// <param name="unit">
        /// The drive number.
        /// </param>
        /// <returns>
        /// True when the image was attached read-only.
        /// </returns>
        public bool IsReadOnly(int unit) => _readOnly;

        /// <summary>
        /// Size of the image in bytes.
        /// </summary>
        /// <param name="unit">
        /// The drive number.
        /// </param>
        /// <returns>
        /// The byte count, 0 without an image.
        /// </returns>
        public long Length(int unit) => _image != null ? _image.Length : 0;

        /// <summary>
        /// Reads bytes from the image.
        /// </summary>
        /// <param name="unit">
        /// The drive number.
        /// </param>
        /// <param name="position">
        /// Byte offset of the block.
        /// </param>
        /// <param name="byteCount">
        /// Number of bytes.
        /// </param>
        /// <returns>
        /// The bytes, or null for another drive or a position outside the image.
        /// </returns>
        public byte[]? ReadBlock(int unit, long position, int byteCount)
        {
            if (unit != 0 || _image == null || position < 0) return null;
            byte[] buf = new byte[byteCount];
            for (int i = 0; i < byteCount; i++)
            {
                long src = position + i;
                buf[i] = src < _image.Length ? _image[src] : (byte)0; // zero-fill past end
            }
            return buf;
        }

        /// <summary>
        /// Writes bytes into the image unless it is read-only.
        /// </summary>
        /// <param name="unit">
        /// The drive number.
        /// </param>
        /// <param name="position">
        /// Byte offset of the block.
        /// </param>
        /// <param name="data">
        /// The bytes to write.
        /// </param>
        public void WriteBlock(int unit, long position, byte[] data)
        {
            if (unit != 0 || _image == null || _readOnly || position < 0) return;
            for (int i = 0; i < data.Length && position + i < _image.Length; i++)
            {
                _image[position + i] = data[i];
            }
        }
    }
}

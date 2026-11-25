using System;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Little-endian binary reader over a <see cref="Stream"/> with its own read buffer (Unity-free, no Span).
    ///
    /// Every Assetto Corsa binary format (kn5, ksanim, knh, ai, acd, FSB5) is little-endian with strings stored as
    /// <c>int32 byteLength + bytes</c> (no terminator). This reader keeps a 64 KB (default) buffer so the many tiny reads of a
    /// node tree do not hit the stream one by one, and bulk reads (<see cref="ReadFloats"/>, <see cref="ReadUShorts"/>) copy
    /// straight from the buffer with <see cref="Buffer.BlockCopy"/> - no per-value BitConverter, no per-call allocation.
    ///
    /// Seeking (<see cref="Position"/>, <see cref="Skip"/>) inside the buffered window does not touch the stream; outside it
    /// the buffer is dropped and the stream is repositioned (the stream must be seekable for that).
    /// Not thread-safe: one reader per thread / stream.
    /// </summary>
    public sealed class AcBinary : IDisposable
    {
        readonly Stream _s;
        readonly bool _own;
        readonly byte[] _buf;
        long _bufStart;     // stream position of _buf[0]
        int _bufLen;        // valid bytes in _buf
        int _pos;           // read cursor in _buf
        long _length = -1;

        /// <summary>Strings longer than this are treated as corrupt data (kn5/ksanim names are &lt; 300 bytes).</summary>
        public int MaxStringBytes = 1 << 20; // ours

        public AcBinary(Stream stream, bool ownsStream = true, int bufferSize = 1 << 16)
        {
            _s = stream ?? throw new ArgumentNullException(nameof(stream));
            _own = ownsStream;
            _buf = new byte[Math.Max(bufferSize, 64)];
            _bufStart = stream.CanSeek ? stream.Position : 0;
        }

        /// <summary>Opens a file for sequential reading (shared read, large OS read-ahead).</summary>
        public static AcBinary OpenFile(string path, int bufferSize = 1 << 16)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
            return new AcBinary(fs, true, bufferSize);
        }

        /// <summary>Reader over an in-memory byte array.</summary>
        public static AcBinary FromBytes(byte[] data, int offset = 0, int count = -1)
        {
            if (count < 0) count = data.Length - offset;
            return new AcBinary(new MemoryStream(data, offset, count, false), true, 1 << 12);
        }

        public Stream BaseStream { get { return _s; } }

        public long Length
        {
            get { if (_length < 0) _length = _s.Length; return _length; }
        }

        public long Position
        {
            get { return _bufStart + _pos; }
            set
            {
                if (value >= _bufStart && value <= _bufStart + _bufLen) { _pos = (int)(value - _bufStart); return; }
                _s.Position = value;
                _bufStart = value; _bufLen = 0; _pos = 0;
            }
        }

        public long Remaining { get { return Length - Position; } }
        public bool AtEnd { get { return Position >= Length; } }

        public void Skip(long bytes)
        {
            if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            if (_pos + bytes <= _bufLen) { _pos += (int)bytes; return; }
            long target = Position + bytes;
            if (target > Length) throw new EndOfStreamException("skip past end of data");
            Position = target;
        }

        /// <summary>Makes at least <paramref name="n"/> bytes (n &lt;= buffer size) available at _pos, or throws.</summary>
        void Need(int n)
        {
            if (_bufLen - _pos >= n) return;
            int keep = _bufLen - _pos;
            if (keep > 0) Buffer.BlockCopy(_buf, _pos, _buf, 0, keep);
            _bufStart += _pos; _pos = 0; _bufLen = keep;
            while (_bufLen < n)
            {
                int r = _s.Read(_buf, _bufLen, _buf.Length - _bufLen);
                if (r <= 0) throw new EndOfStreamException("unexpected end of data at " + (_bufStart + _bufLen));
                _bufLen += r;
            }
        }

        public byte ReadByte() { Need(1); return _buf[_pos++]; }
        public bool ReadBool() { Need(1); return _buf[_pos++] != 0; }

        public short ReadInt16() { Need(2); short v = (short)(_buf[_pos] | (_buf[_pos + 1] << 8)); _pos += 2; return v; }
        public ushort ReadUInt16() { Need(2); ushort v = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8)); _pos += 2; return v; }

        public int ReadInt32()
        {
            Need(4);
            int v = _buf[_pos] | (_buf[_pos + 1] << 8) | (_buf[_pos + 2] << 16) | (_buf[_pos + 3] << 24);
            _pos += 4; return v;
        }

        public uint ReadUInt32() { return (uint)ReadInt32(); }

        public long ReadInt64()
        {
            uint lo = ReadUInt32(); uint hi = ReadUInt32();
            return (long)(((ulong)hi << 32) | lo);
        }

        public ulong ReadUInt64() { return (ulong)ReadInt64(); }

        public float ReadSingle()
        {
            Need(4);
            float v = BitConverter.ToSingle(_buf, _pos); // little-endian host (Windows x64 / Unity players)
            _pos += 4; return v;
        }

        /// <summary>AC string: int32 byte length + UTF-8 bytes (Latin-1 fallback for invalid UTF-8).</summary>
        public string ReadString()
        {
            int n = ReadInt32();
            if (n < 0 || n > MaxStringBytes) throw new InvalidDataException("bad string length " + n + " at " + (Position - 4));
            if (n == 0) return string.Empty;
            if (n <= _buf.Length)
            {
                Need(n);
                string s = AcText.Decode(_buf, _pos, n);
                _pos += n; return s;
            }
            return AcText.Decode(ReadBytes(n), 0, n);
        }

        /// <summary>Fixed-length ASCII (e.g. a magic tag).</summary>
        public string ReadAscii(int n)
        {
            Need(n);
            string s = Encoding.ASCII.GetString(_buf, _pos, n);
            _pos += n; return s;
        }

        public byte[] ReadBytes(int n)
        {
            if (n < 0) throw new InvalidDataException("negative byte count " + n);
            var b = new byte[n];
            ReadBytes(b, 0, n);
            return b;
        }

        /// <summary>Copies <paramref name="count"/> bytes into <paramref name="dst"/> (bypasses the buffer for big blocks).</summary>
        public void ReadBytes(byte[] dst, int offset, int count)
        {
            int have = _bufLen - _pos;
            if (have >= count) { Buffer.BlockCopy(_buf, _pos, dst, offset, count); _pos += count; return; }
            if (have > 0) { Buffer.BlockCopy(_buf, _pos, dst, offset, have); _pos += have; offset += have; count -= have; }
            // buffer is empty now: read directly
            _bufStart += _pos; _pos = 0; _bufLen = 0;
            while (count > 0)
            {
                int r = _s.Read(dst, offset, count);
                if (r <= 0) throw new EndOfStreamException("unexpected end of data at " + _bufStart);
                offset += r; count -= r; _bufStart += r;
            }
        }

        /// <summary>Reads <paramref name="count"/> little-endian floats into <paramref name="dst"/> starting at <paramref name="offset"/>.</summary>
        public void ReadFloats(float[] dst, int offset, int count) { ReadBlock(dst, offset * 4, count * 4); }

        /// <summary>Reads <paramref name="count"/> little-endian uint16 into <paramref name="dst"/>.</summary>
        public void ReadUShorts(ushort[] dst, int offset, int count) { ReadBlock(dst, offset * 2, count * 2); }

        /// <summary>Reads <paramref name="count"/> little-endian int32 into <paramref name="dst"/>.</summary>
        public void ReadInts(int[] dst, int offset, int count) { ReadBlock(dst, offset * 4, count * 4); }

        public float[] ReadFloats(int count) { var f = new float[count]; ReadFloats(f, 0, count); return f; }

        /// <summary>Block copy of raw bytes into a primitive array (dstByteOffset/byteCount in bytes).</summary>
        void ReadBlock(Array dst, int dstByteOffset, int byteCount)
        {
            if (byteCount < 0) throw new InvalidDataException("negative count");
            while (byteCount > 0)
            {
                if (_pos >= _bufLen) Need(1);
                int n = Math.Min(byteCount, _bufLen - _pos);
                Buffer.BlockCopy(_buf, _pos, dst, dstByteOffset, n);
                _pos += n; dstByteOffset += n; byteCount -= n;
            }
        }

        public void Dispose()
        {
            if (_own) _s.Dispose();
        }
    }
}

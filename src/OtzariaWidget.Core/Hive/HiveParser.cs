using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace OtzariaWidget.Core.Hive;

/// <summary>Outcome of parsing a hive_ce box file (see docs/research/hive-and-progress.md section 1).</summary>
public sealed record HiveParseResult(
    IReadOnlyDictionary<string, object?> Values,
    int FrameCount,
    int TombstoneCount,
    int SkippedValueCount,
    long ParsedBytes,
    long TotalBytes,
    string? Error)
{
    /// <summary>True when every byte of the file was consumed by CRC-valid frames.</summary>
    public bool IsClean => Error is null;
}

/// <summary>Pure in-memory hive_ce frame parser. Never throws on malformed data; stops at the first bad frame.</summary>
public static class HiveParser
{
    private const int MaxDepth = 64;

    public static HiveParseResult Parse(byte[] data, Func<string, bool>? keyFilter = null)
    {
        keyFilter ??= static _ => true;
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        int pos = 0, frames = 0, tombstones = 0, skipped = 0;
        string? error = null;
        int n = data.Length;

        while (n - pos >= 4)
        {
            uint lenU = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            if (lenU < 8 || lenU > (uint)(n - pos))
            {
                error = lenU < 8 ? "invalid frame length" : "truncated frame";
                break;
            }
            int len = (int)lenU;
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + len - 4));
            if (Crc32.Compute(data.AsSpan(pos, len - 4)) != crc)
            {
                error = "CRC mismatch";
                break;
            }

            var r = new Cursor(data, pos + 4, pos + len - 4);
            string? key = null;
            try { key = ReadKey(ref r); }
            catch (FormatException) { skipped++; }

            if (key is not null && keyFilter(key))
            {
                if (r.Pos == r.Limit)
                {
                    map.Remove(key);
                    tombstones++;
                }
                else
                {
                    try
                    {
                        map[key] = ReadValue(ref r, 0);
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                    {
                        // Unknown custom adapter / corrupt value: the frame length tells us where the next frame starts.
                        // The old value is superseded and unreadable, so drop it.
                        map.Remove(key);
                        skipped++;
                    }
                }
            }
            frames++;
            pos += len;
        }

        if (error is null && pos != n)
            error = "partial length prefix at end of file";

        return new HiveParseResult(map, frames, tombstones, skipped, pos, n, error);
    }

    private static string ReadKey(ref Cursor r)
    {
        int t = r.U8();
        if (t == 0) return r.U32().ToString(CultureInfo.InvariantCulture);
        if (t == 1) return r.Str(r.U8());
        throw new FormatException("unsupported key type");
    }

    private static object ReadKeyValue(ref Cursor r)
    {
        int t = r.U8();
        if (t == 0) return (long)r.U32();
        if (t == 1) return r.Str(r.U8());
        throw new FormatException("unsupported key type");
    }

    private static long ReadHiveInt(ref Cursor r)
    {
        double d = r.F64();
        if (double.IsNaN(d) || d >= 9.2233720368547758E18 || d <= -9.2233720368547758E18)
            throw new FormatException("int out of range");
        return (long)d; // truncation toward zero, like Dart's double.toInt()
    }

    private static int ReadCount(ref Cursor r, int minElementBytes)
    {
        uint c = r.U32();
        if (c > (uint)int.MaxValue || (long)c * minElementBytes > r.Remaining)
            throw new FormatException("bad element count");
        return (int)c;
    }

    private static object? ReadValue(ref Cursor r, int depth)
    {
        if (depth > MaxDepth) throw new FormatException("nesting too deep");
        int t = r.U8();
        if (t == 21) t = r.U16();
        switch (t)
        {
            case 0: return null;
            case 1: return ReadHiveInt(ref r);
            case 2: return r.F64();
            case 3: return r.U8() > 0;
            case 4: return r.Str(r.U32AsLength());
            case 5: return r.Bytes(r.U32AsLength());
            case 6:
            case 13:
            {
                int c = ReadCount(ref r, 8);
                var l = new List<object?>(c);
                for (int i = 0; i < c; i++) l.Add(ReadHiveInt(ref r));
                return l;
            }
            case 7:
            case 14:
            {
                int c = ReadCount(ref r, 8);
                var l = new List<object?>(c);
                for (int i = 0; i < c; i++) l.Add(r.F64());
                return l;
            }
            case 8:
            {
                int c = ReadCount(ref r, 1);
                var l = new List<object?>(c);
                for (int i = 0; i < c; i++) l.Add(r.U8() > 0);
                return l;
            }
            case 9:
            case 15:
            {
                int c = ReadCount(ref r, 4);
                var l = new List<object?>(c);
                for (int i = 0; i < c; i++) l.Add(r.Str(r.U32AsLength()));
                return l;
            }
            case 10:
            case 19:
            {
                int c = ReadCount(ref r, 1);
                var l = new List<object?>(c);
                for (int i = 0; i < c; i++) l.Add(ReadValue(ref r, depth + 1));
                return l;
            }
            case 11:
            {
                int c = ReadCount(ref r, 2);
                var m = new Dictionary<object, object?>(c);
                for (int i = 0; i < c; i++)
                {
                    object? k = ReadValue(ref r, depth + 1);
                    object? v = ReadValue(ref r, depth + 1);
                    if (k is null) continue;
                    if (k is List<object?> or Dictionary<object, object?> or byte[])
                        k = k.ToString() ?? "";
                    m[k] = v;
                }
                return m;
            }
            case 12: // HiveList: u32 count, u8 boxNameLen + name, count x key
            {
                int c = ReadCount(ref r, 2);
                r.Str(r.U8());
                var l = new List<object?>(c);
                for (int i = 0; i < c; i++) l.Add(ReadKeyValue(ref r));
                return l;
            }
            case 16:
                return DateTime.UnixEpoch.AddMilliseconds(ReadHiveInt(ref r));
            case 17:
            {
                var s = r.Str(r.U8());
                if (!BigInteger.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var b))
                    throw new FormatException("bad bigint");
                return b >= long.MinValue && b <= long.MaxValue ? (object)(long)b : b;
            }
            case 18:
            {
                double ms = r.F64();
                bool utc = r.U8() > 0;
                if (double.IsNaN(ms) || Math.Abs(ms) > 2.5e14) throw new FormatException("bad datetime");
                var d = DateTime.UnixEpoch.AddTicks((long)Math.Round(ms * TimeSpan.TicksPerMillisecond));
                return utc ? d : d.ToLocalTime();
            }
            case 20:
            {
                double ms = r.F64();
                if (double.IsNaN(ms) || Math.Abs(ms) > 9e14) throw new FormatException("bad duration");
                return TimeSpan.FromTicks((long)Math.Round(ms * TimeSpan.TicksPerMillisecond));
            }
            default:
                throw new FormatException("unknown type id (custom adapter)");
        }
    }

    private struct Cursor
    {
        private readonly byte[] _b;
        public int Pos;
        public readonly int Limit;

        public Cursor(byte[] b, int pos, int limit)
        {
            _b = b;
            Pos = pos;
            Limit = limit;
        }

        public readonly int Remaining => Limit - Pos;

        private readonly void Need(int n)
        {
            if (n < 0 || n > Limit - Pos) throw new FormatException("not enough bytes");
        }

        public byte U8()
        {
            Need(1);
            return _b[Pos++];
        }

        public ushort U16()
        {
            Need(2);
            var v = BinaryPrimitives.ReadUInt16LittleEndian(_b.AsSpan(Pos));
            Pos += 2;
            return v;
        }

        public uint U32()
        {
            Need(4);
            var v = BinaryPrimitives.ReadUInt32LittleEndian(_b.AsSpan(Pos));
            Pos += 4;
            return v;
        }

        public int U32AsLength()
        {
            uint v = U32();
            if (v > (uint)int.MaxValue) throw new FormatException("bad length");
            return (int)v;
        }

        public double F64()
        {
            Need(8);
            var v = BinaryPrimitives.ReadDoubleLittleEndian(_b.AsSpan(Pos));
            Pos += 8;
            return v;
        }

        public string Str(int n)
        {
            Need(n);
            var s = Encoding.UTF8.GetString(_b, Pos, n);
            Pos += n;
            return s;
        }

        public byte[] Bytes(int n)
        {
            Need(n);
            var a = _b.AsSpan(Pos, n).ToArray();
            Pos += n;
            return a;
        }
    }
}

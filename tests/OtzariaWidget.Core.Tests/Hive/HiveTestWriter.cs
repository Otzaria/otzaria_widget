using System.Buffers.Binary;
using System.Text;

namespace OtzariaWidget.Core.Tests.Hive;

/// <summary>
/// TEST-ONLY hive_ce box writer used to synthesize .hive files (all value types, tombstones, corruption).
/// Deliberately independent from the production parser (own CRC32 implementation).
/// </summary>
public sealed class HiveTestWriter
{
    private readonly MemoryStream _ms = new();

    public byte[] ToArray() => _ms.ToArray();
    public int Length => (int)_ms.Length;

    public void Save(string path) => File.WriteAllBytes(path, ToArray());

    // ----- frames -------------------------------------------------------------------------

    public HiveTestWriter Put(string key, byte[] value) => Append(Frame(KeyStr(key), value));
    public HiveTestWriter Put(uint key, byte[] value) => Append(Frame(KeyInt(key), value));
    public HiveTestWriter Delete(string key) => Append(Frame(KeyStr(key), []));
    public HiveTestWriter Delete(uint key) => Append(Frame(KeyInt(key), []));
    public HiveTestWriter AppendRaw(byte[] bytes) => Append(bytes);

    public HiveTestWriter Append(byte[] frame)
    {
        _ms.Write(frame);
        return this;
    }

    public static byte[] Frame(byte[] key, byte[] value)
    {
        int len = 4 + key.Length + value.Length + 4;
        var buf = new byte[len];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, (uint)len);
        key.CopyTo(buf, 4);
        value.CopyTo(buf, 4 + key.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(len - 4), Crc32(buf.AsSpan(0, len - 4)));
        return buf;
    }

    public static byte[] KeyStr(string k)
    {
        var b = Encoding.UTF8.GetBytes(k);
        var r = new byte[2 + b.Length];
        r[0] = 1;
        r[1] = (byte)b.Length;
        b.CopyTo(r, 2);
        return r;
    }

    public static byte[] KeyInt(uint k)
    {
        var r = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(1), k);
        return r;
    }

    // ----- values (each returns typeId + payload) -----------------------------------------

    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] B(params byte[] b) => b;
    private static byte[] U16(int v) { var r = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(r, (ushort)v); return r; }
    private static byte[] U32(uint v) { var r = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(r, v); return r; }
    private static byte[] F64(double v) { var r = new byte[8]; BinaryPrimitives.WriteDoubleLittleEndian(r, v); return r; }
    private static byte[] Utf8Len(string s) { var b = Encoding.UTF8.GetBytes(s); return Cat(U32((uint)b.Length), b); }

    public static byte[] Null() => B(0);
    public static byte[] Int(long v) => Cat(B(1), F64(v));
    public static byte[] IntFromRawDouble(double v) => Cat(B(1), F64(v));
    public static byte[] Double(double v) => Cat(B(2), F64(v));
    public static byte[] Bool(bool v) => B(3, (byte)(v ? 1 : 0));
    public static byte[] Str(string v) => Cat(B(4), Utf8Len(v));
    public static byte[] Bytes(byte[] v) => Cat(B(5), U32((uint)v.Length), v);
    public static byte[] IntList(params long[] v) => Cat(B(6), U32((uint)v.Length), Cat(v.Select(x => F64(x)).ToArray()));
    public static byte[] DoubleList(params double[] v) => Cat(B(7), U32((uint)v.Length), Cat(v.Select(x => F64(x)).ToArray()));
    public static byte[] BoolList(params bool[] v) => Cat(B(8), U32((uint)v.Length), v.Select(x => (byte)(x ? 1 : 0)).ToArray());
    public static byte[] StrList(params string[] v) => Cat(B(9), U32((uint)v.Length), Cat(v.Select(Utf8Len).ToArray()));
    public static byte[] List(params byte[][] items) => Cat(B(10), U32((uint)items.Length), Cat(items));
    public static byte[] Map(params (byte[] Key, byte[] Value)[] entries)
        => Cat(B(11), U32((uint)entries.Length), Cat(entries.SelectMany(e => new[] { e.Key, e.Value }).ToArray()));
    public static byte[] HiveList(string box, params string[] stringKeys)
    {
        var name = Encoding.ASCII.GetBytes(box);
        return Cat(B(12), U32((uint)stringKeys.Length), B((byte)name.Length), name, Cat(stringKeys.Select(KeyStr).ToArray()));
    }
    public static byte[] IntSet(params long[] v) => Cat(B(13), U32((uint)v.Length), Cat(v.Select(x => F64(x)).ToArray()));
    public static byte[] DoubleSet(params double[] v) => Cat(B(14), U32((uint)v.Length), Cat(v.Select(x => F64(x)).ToArray()));
    public static byte[] StrSet(params string[] v) => Cat(B(15), U32((uint)v.Length), Cat(v.Select(Utf8Len).ToArray()));
    public static byte[] DateTimeMs(long ms) => Cat(B(16), F64(ms));
    public static byte[] BigInt(string decimalString)
    {
        var s = Encoding.ASCII.GetBytes(decimalString);
        return Cat(B(17), B((byte)s.Length), s);
    }
    public static byte[] DateTimeTz(double ms, bool utc) => Cat(B(18), F64(ms), B((byte)(utc ? 1 : 0)));
    public static byte[] Set(params byte[][] items) => Cat(B(19), U32((uint)items.Length), Cat(items));
    public static byte[] Duration(double ms) => Cat(B(20), F64(ms));
    /// <summary>Type-id extension (21) followed by u16 real id and an opaque payload (custom adapter).</summary>
    public static byte[] Ext(int typeId, params byte[] payload) => Cat(B(21), U16(typeId), payload);

    // ----- CRC32 (zlib) -------------------------------------------------------------------

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        uint c = (uint)i;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}

/// <summary>Creates and cleans up temp files for tests.</summary>
public sealed class HiveTempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "otzwidget_" + Guid.NewGuid().ToString("N"));

    public HiveTempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string Write(string name, byte[] bytes)
    {
        var p = File(name);
        System.IO.File.WriteAllBytes(p, bytes);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { /* best effort */ }
    }
}

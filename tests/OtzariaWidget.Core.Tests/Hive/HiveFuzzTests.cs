using System.Buffers.Binary;
using System.Diagnostics;
using OtzariaWidget.Core.Hive;
using OtzariaWidget.Core.Preferences;
using OtzariaWidget.Core.Progress;
using Xunit.Abstractions;
using W = OtzariaWidget.Core.Tests.Hive.HiveTestWriter;

namespace OtzariaWidget.Core.Tests.Hive;

/// <summary>
/// Deterministic fuzzing of the hive parser (and the layers above it): random truncation, byte flips, inserted garbage,
/// absurd length prefixes, and CRC-repaired payload corruption (so the mutation gets past the checksum gate).
/// Contract: never throws, never hangs, never allocates absurd memory. Iterations: env OTZ_FUZZ_ITER (default 1200).
/// The real %APPDATA% file is only ever read; mutations happen on in-memory copies / temp files.
/// </summary>
public class HiveFuzzTests(ITestOutputHelper output)
{
    private static int Iterations
        => int.TryParse(Environment.GetEnvironmentVariable("OTZ_FUZZ_ITER"), out var n) && n > 0 ? n : 1200;

    private static byte[] SyntheticFile()
    {
        var w = new HiveTestWriter()
            .Put("key-selected-city", W.Str("ירושלים"))
            .Put("key-dark-mode", W.Bool(true))
            .Put("key-swatch-color", W.Int(0xFF2C1B02))
            .Put("key-calendar-enabled-zmanim", W.Str("[\"sunrise\",\"sunset\"]"))
            .Put("sz:tracked_books", W.Str("[1,2,3]"))
            .Put("sz:progress_by_id", W.Str("{\"1\":{\"0\":{\"learn\":true},\"1\":{\"learn\":true,\"review1\":true}}}"))
            .Put("sz:book_columns", W.Str("{\"1\":[{\"id\":\"learn\",\"label\":\"x\"}]}"))
            .Put("il", W.IntList(1, 2, 3))
            .Put("dl", W.DoubleList(1.5))
            .Put("bl", W.BoolList(true, false))
            .Put("sl", W.StrList("a", "b"))
            .Put("dyn", W.List(W.Int(1), W.Str("x"), W.List(W.Null()), W.Map((W.Str("k"), W.Int(1)))))
            .Put("hl", W.HiveList("box", "a", "b"))
            .Put("dt", W.DateTimeMs(1_700_000_000_000))
            .Put("dtz", W.DateTimeTz(1_700_000_000_000.5, false))
            .Put("big", W.BigInt("123456789012345678901234567890"))
            .Put("dur", W.Duration(1234.5))
            .Put("bytes", W.Bytes([1, 2, 3]))
            .Put("custom", W.Ext(300, 1, 2, 3))
            .Put("k", W.Str("old")).Put("k", W.Str("new")).Delete("k")
            .Put(7u, W.Str("int key"));
        return w.ToArray();
    }

    private static List<(int Offset, int Length)> Frames(byte[] data)
    {
        var list = new List<(int, int)>();
        int pos = 0;
        while (data.Length - pos >= 8)
        {
            uint len = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            if (len < 8 || len > data.Length - pos) break;
            list.Add((pos, (int)len));
            pos += (int)len;
        }
        return list;
    }

    private static void FixCrc(byte[] data, int offset, int length)
        => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + length - 4), W.Crc32(data.AsSpan(offset, length - 4)));

    private static byte[] Mutate(byte[] original, Random rnd)
    {
        var data = (byte[])original.Clone();
        var frames = Frames(original);
        int ops = rnd.Next(1, 4);
        for (int i = 0; i < ops && data.Length > 0; i++)
        {
            switch (rnd.Next(7))
            {
                case 0: // truncate
                    data = data[..rnd.Next(0, data.Length)];
                    break;
                case 1: // flip random bytes (CRC will normally catch it)
                    for (int k = rnd.Next(1, 6); k > 0 && data.Length > 0; k--)
                        data[rnd.Next(data.Length)] ^= (byte)rnd.Next(1, 256);
                    break;
                case 2: // corrupt a frame body and repair its CRC so the value decoder sees the damage
                    if (frames.Count > 0)
                    {
                        var (off, len) = frames[rnd.Next(frames.Count)];
                        if (off + len <= data.Length && len > 8)
                        {
                            for (int k = rnd.Next(1, 4); k > 0; k--)
                                data[off + 4 + rnd.Next(len - 8)] = (byte)rnd.Next(256);
                            FixCrc(data, off, len);
                        }
                    }
                    break;
                case 3: // absurd length prefix on a frame (CRC repaired or not)
                    if (frames.Count > 0)
                    {
                        var (off, _) = frames[rnd.Next(frames.Count)];
                        if (off + 4 > data.Length) break;
                        uint[] evil = [0, 1, 7, 8, 9, 0xFFFFFFFF, 0x7FFFFFFF, 0x80000000, (uint)data.Length + 1, 1u << 30];
                        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(off), evil[rnd.Next(evil.Length)]);
                    }
                    break;
                case 4: // insert random garbage
                {
                    var junk = new byte[rnd.Next(1, 64)];
                    rnd.NextBytes(junk);
                    int at = rnd.Next(data.Length + 1);
                    data = [.. data[..at], .. junk, .. data[at..]];
                    break;
                }
                case 5: // absurd count / length inside a CRC-repaired frame
                    if (frames.Count > 0)
                    {
                        var (off, len) = frames[rnd.Next(frames.Count)];
                        if (off + len <= data.Length && len >= 16)
                        {
                            int at = off + 4 + rnd.Next(len - 12);
                            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), rnd.Next(2) == 0 ? 0xFFFFFFFFu : (uint)rnd.Next(int.MaxValue));
                            FixCrc(data, off, len);
                        }
                    }
                    break;
                default: // duplicate a random slice
                {
                    int a = rnd.Next(data.Length), b = rnd.Next(a, data.Length);
                    data = [.. data, .. data[a..b]];
                    break;
                }
            }
        }
        return data;
    }

    private static void RunAllLayers(byte[] data, string label)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        // raw parser, once with everything materialized and once through the privacy whitelist
        var all = HiveParser.Parse(data, _ => true);
        var filtered = HiveParser.Parse(data, PreferencesReader.IsWhitelisted);
        Assert.True(all.ParsedBytes <= data.Length, label);
        Assert.All(filtered.Values.Keys, k => Assert.True(PreferencesReader.IsWhitelisted(k), k));

        // upper layers on whatever survived
        var prefs = PreferencesReader.Map(filtered.Values);
        var progress = new ProgressService().Compute(prefs, null);
        Assert.NotNull(progress);

        sw.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sw.ElapsedMilliseconds < 5000, $"{label}: too slow ({sw.ElapsedMilliseconds} ms)");
        Assert.True(allocated < 64L * data.Length + 32L * 1024 * 1024, $"{label}: allocated {allocated} bytes for {data.Length} input bytes");
    }

    [Fact]
    public void Fuzz_synthetic_file_never_throws_hangs_or_balloons()
    {
        var original = SyntheticFile();
        var rnd = new Random(20260713);
        RunAllLayers(original, "pristine");
        for (int i = 0; i < Iterations; i++)
        {
            var data = Mutate(original, rnd);
            try { RunAllLayers(data, $"iter {i}"); }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), $"otz_fuzz_fail_{i}.bin"), data);
                throw new Exception($"iteration {i} threw {ex.GetType().Name}: {ex.Message}", ex);
            }
        }
        output.WriteLine($"synthetic: {Iterations} iterations OK");
    }

    [Fact]
    public void Fuzz_every_truncation_and_single_byte_corruption_of_synthetic_file()
    {
        var original = SyntheticFile();
        for (int cut = 0; cut <= original.Length; cut++)
            RunAllLayers(original[..cut], $"cut {cut}");
        for (int i = 0; i < original.Length; i++)
        {
            var d = (byte[])original.Clone();
            d[i] ^= 0xA5;
            RunAllLayers(d, $"flip {i}");
        }
    }

    [Fact]
    public void Fuzz_copy_of_real_app_preferences_never_throws()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData)) return;
        var real = Path.Combine(appData, "otzaria", "app_preferences.hive");
        if (!File.Exists(real)) return;

        using var dir = new HiveTempDir();
        var copy = dir.Path + Path.DirectorySeparatorChar + "copy.hive";
        File.Copy(real, copy);
        var bytes = File.ReadAllBytes(copy);

        var rnd = new Random(424242);
        int n = Math.Max(300, Iterations / 2);
        for (int i = 0; i < n; i++)
        {
            var data = Mutate(bytes, rnd);
            File.WriteAllBytes(copy, data);
            // through the real file-based reader too
            var res = new HiveReader().ReadDetailed(copy, PreferencesReader.IsWhitelisted);
            Assert.NotNull(res);
            RunAllLayers(data, $"real iter {i}");
        }
        // every truncation point of the real copy
        for (int cut = 0; cut <= bytes.Length; cut += Math.Max(1, bytes.Length / 400))
            RunAllLayers(bytes[..cut], $"real cut {cut}");

        output.WriteLine($"real copy: {n} mutations + truncations OK ({bytes.Length} bytes)");
    }
}

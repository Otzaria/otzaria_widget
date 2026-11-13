using System.Buffers.Binary;
using System.Diagnostics;
using OtzariaWidget.Core.Hive;
using W = OtzariaWidget.Core.Tests.Hive.HiveTestWriter;

namespace OtzariaWidget.Core.Tests.Hive;

/// <summary>QA regression tests: behaviors verified against hive_ce 2.22 binary_reader_impl.dart / frame_helper.dart.</summary>
public class HiveParserHardeningTests
{
    private static readonly Func<string, bool> All = _ => true;

    private static HiveParseResult Parse(HiveTestWriter w) => HiveParser.Parse(w.ToArray(), All);

    private static byte[] F64(double d)
    {
        var r = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(r, d);
        return r;
    }

    [Fact]
    public void Extension_id_can_wrap_internal_adapter_ids_and_builtin_ids()
    {
        // hive: readTypeId() => 21 + word; the real id is then dispatched like a plain one (builtin switch, else registry).
        var w = new HiveTestWriter()
            .Put("dt", W.Ext(16, F64(1_700_000_000_000)))
            .Put("int", W.Ext(1, F64(41)))
            .Put("nested21", W.Ext(21, 1, 2, 3));
        var res = Parse(w);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000).UtcDateTime, res.Values["dt"]);
        Assert.Equal(41L, res.Values["int"]);
        Assert.False(res.Values.ContainsKey("nested21"));
        Assert.Equal(1, res.SkippedValueCount);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(255)]
    [InlineData(300)]
    [InlineData(65535)]
    public void Reserved_and_custom_type_ids_are_skipped_using_the_frame_length(int typeId)
    {
        var w = new HiveTestWriter()
            .Put("a", W.Str("1"))
            .Put("x", typeId < 256 ? [(byte)typeId, 9, 9, 9] : W.Ext(typeId, 9, 9, 9))
            .Put("b", W.Str("2"));
        var res = Parse(w);
        Assert.True(res.IsClean);
        Assert.Equal(1, res.SkippedValueCount);
        Assert.Equal("1", res.Values["a"]);
        Assert.Equal("2", res.Values["b"]);
        Assert.False(res.Values.ContainsKey("x"));
    }

    [Fact]
    public void Bytes_after_the_value_inside_a_frame_are_ignored_like_hive_skip_availableBytes()
    {
        var f = HiveTestWriter.Frame(W.KeyStr("k"), [.. W.Str("v"), 0xDE, 0xAD, 0xBE, 0xEF]);
        var res = HiveParser.Parse([.. f, .. HiveTestWriter.Frame(W.KeyStr("n"), W.Int(1))], All);
        Assert.True(res.IsClean);
        Assert.Equal("v", res.Values["k"]);
        Assert.Equal(1L, res.Values["n"]);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public void Frames_too_short_to_hold_a_key_are_skipped_not_fatal(int len)
    {
        // hive: frameLength >= 8 passes the length check, readKey then throws; we skip the frame and continue
        var bad = new byte[len];
        BinaryPrimitives.WriteUInt32LittleEndian(bad, (uint)len);
        for (int i = 4; i < len - 4; i++) bad[i] = 0xEE;
        BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(len - 4), W.Crc32(bad.AsSpan(0, len - 4)));
        var data = new HiveTestWriter().Put("a", W.Str("1")).AppendRaw(bad).Put("b", W.Str("2")).ToArray();
        var res = HiveParser.Parse(data, All);
        Assert.True(res.IsClean);
        Assert.Equal("1", res.Values["a"]);
        Assert.Equal("2", res.Values["b"]);
        Assert.Equal(1, res.SkippedValueCount);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(4u)]
    [InlineData(7u)]
    public void Frame_length_below_8_stops_parsing(uint len)
    {
        var f1 = HiveTestWriter.Frame(W.KeyStr("one"), W.Str("1"));
        var junk = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(junk, len);
        var res = HiveParser.Parse([.. f1, .. junk], All);
        Assert.Equal("invalid frame length", res.Error);
        Assert.Equal("1", res.Values["one"]);
        Assert.Equal(f1.Length, res.ParsedBytes);
    }

    [Fact]
    public void Frame_length_pointing_beyond_eof_is_a_torn_tail()
    {
        var f1 = HiveTestWriter.Frame(W.KeyStr("one"), W.Str("1"));
        var f2 = HiveTestWriter.Frame(W.KeyStr("two"), W.Str("2"));
        BinaryPrimitives.WriteUInt32LittleEndian(f2, (uint)f2.Length + 1);
        var res = HiveParser.Parse([.. f1, .. f2], All);
        Assert.Equal("truncated frame", res.Error);
        Assert.Equal("1", res.Values["one"]);
        Assert.Equal(f1.Length, res.ParsedBytes);

        BinaryPrimitives.WriteUInt32LittleEndian(f2, 0xFFFFFFFFu);
        Assert.Equal("truncated frame", HiveParser.Parse([.. f1, .. f2], All).Error);
    }

    [Fact]
    public void String_key_of_255_bytes_and_empty_key_work()
    {
        var longKey = new string('k', 255);
        var w = new HiveTestWriter().Put(longKey, W.Int(1)).Put("", W.Int(2));
        var v = Parse(w).Values;
        Assert.Equal(1L, v[longKey]);
        Assert.Equal(2L, v[""]);
    }

    [Fact]
    public void Unknown_key_type_tag_skips_the_frame()
    {
        var bad = HiveTestWriter.Frame([2, 1, (byte)'x'], W.Int(1)); // key tag 2 is neither uint(0) nor string(1)
        var data = new HiveTestWriter().Put("a", W.Int(1)).AppendRaw(bad).Put("b", W.Int(2)).ToArray();
        var res = HiveParser.Parse(data, All);
        Assert.True(res.IsClean);
        Assert.Equal(1, res.SkippedValueCount);
        Assert.Equal(2, res.Values.Count);
    }

    [Fact]
    public void Int_out_of_int64_range_or_nan_is_dropped_not_thrown()
    {
        var w = new HiveTestWriter()
            .Put("huge", W.IntFromRawDouble(1e30))
            .Put("nan", W.IntFromRawDouble(double.NaN))
            .Put("inf", W.IntFromRawDouble(double.NegativeInfinity))
            .Put("ok", W.Int(5));
        var res = Parse(w);
        Assert.Equal(3, res.SkippedValueCount);
        Assert.Equal(5L, res.Values["ok"]);
        Assert.Single(res.Values);
    }

    [Theory]
    [InlineData(1e300)]
    [InlineData(double.NaN)]
    public void Absurd_datetime_and_duration_values_are_dropped_not_thrown(double ms)
    {
        var w = new HiveTestWriter()
            .Put("dt16", W.Ext(16, F64(ms)))
            .Put("dt18", W.DateTimeTz(ms, true))
            .Put("dur", W.Duration(ms))
            .Put("ok", W.Bool(true));
        var res = Parse(w);
        Assert.Equal(true, res.Values["ok"]);
        Assert.Single(res.Values);
        Assert.Equal(3, res.SkippedValueCount);
    }

    [Fact]
    public void Datetime_with_and_without_utc_flag_decode_to_the_same_instant()
    {
        const long ms = 1_700_000_123_456;
        var w = new HiveTestWriter()
            .Put("plain", W.DateTimeMs(ms))
            .Put("tzUtc", W.DateTimeTz(ms, true))
            .Put("tzLocal", W.DateTimeTz(ms, false));
        var v = Parse(w).Values;
        var expected = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        Assert.Equal(expected, ((DateTime)v["plain"]!).ToUniversalTime());
        Assert.Equal(expected, ((DateTime)v["tzUtc"]!).ToUniversalTime());
        Assert.Equal(expected, ((DateTime)v["tzLocal"]!).ToUniversalTime());
        Assert.Equal(DateTimeKind.Utc, ((DateTime)v["tzUtc"]!).Kind);
        Assert.Equal(DateTimeKind.Local, ((DateTime)v["tzLocal"]!).Kind);
    }

    [Fact]
    public void Crc_is_plain_crc32_with_zero_seed_known_vector()
    {
        // "123456789" => 0xCBF43926 (zlib). hive_ce: Crc32.compute(bytes, crc: 0) for unencrypted boxes.
        Assert.Equal(0xCBF43926u, W.Crc32("123456789"u8));
        var f = HiveTestWriter.Frame(W.KeyStr("a"), W.Str("123456789"));
        Assert.True(HiveParser.Parse(f, All).IsClean);
    }

    [Fact]
    public void Malformed_utf8_in_a_string_is_replaced_like_hive_allowMalformed()
    {
        var payload = new byte[] { 4, 2, 0, 0, 0, 0xFF, 0xFE };
        var res = HiveParser.Parse(HiveTestWriter.Frame(W.KeyStr("s"), payload), All);
        Assert.True(res.IsClean);
        Assert.Equal("��", res.Values["s"]);
    }

    [Fact]
    public void Nested_custom_value_inside_a_list_drops_only_that_key()
    {
        var w = new HiveTestWriter()
            .Put("list", W.List(W.Int(1), W.Ext(400, 1, 2)))
            .Put("after", W.Int(2));
        var res = Parse(w);
        Assert.False(res.Values.ContainsKey("list"));
        Assert.Equal(2L, res.Values["after"]);
    }

    // ---------- file-level behaviour ----------

    [Fact]
    public async Task Reader_waits_for_a_short_exclusive_lock_and_then_succeeds()
    {
        using var dir = new HiveTempDir();
        var path = dir.Write("lock.hive", new HiveTestWriter().Put("k", W.Str("v")).ToArray());
        // exclusive handle (AV scan / non-cooperative writer) held ~120 ms; released while the reader is backing off
        var locker = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        // Dedicated thread (not the thread pool, which can be starved under parallel test load)
        // and a start signal, so the release reliably happens inside the reader's ~350 ms retry window.
        using var started = new ManualResetEventSlim();
        var releaser = new Thread(() => { started.Set(); Thread.Sleep(120); locker.Dispose(); }) { IsBackground = true };
        releaser.Start();
        started.Wait();
        var sw = Stopwatch.StartNew();
        var values = new HiveReader().Read(path, All);
        releaser.Join();
        await Task.CompletedTask;
        Assert.Equal("v", values["k"]);
        Assert.True(sw.ElapsedMilliseconds < 3000);
    }

    [Fact]
    public void Reader_gives_up_with_an_empty_result_when_the_file_stays_locked()
    {
        using var dir = new HiveTempDir();
        var path = dir.Write("lock.hive", new HiveTestWriter().Put("k", W.Str("v")).ToArray());
        using var locker = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var sw = Stopwatch.StartNew();
        var res = new HiveReader().ReadDetailed(path, All);
        Assert.Empty(res.Values);
        Assert.Equal("file not readable", res.Error);
        Assert.True(sw.ElapsedMilliseconds < 3000, "bounded retry");
    }

    [Fact]
    public void Missing_file_does_not_pay_the_full_retry_backoff()
    {
        using var dir = new HiveTempDir();
        var res = new HiveReader().ReadDetailed(dir.File("nope.hive"), All); // warm-up (JIT)
        var sw = Stopwatch.StartNew();
        res = new HiveReader().ReadDetailed(dir.File("nope.hive"), All);
        Assert.Equal("file not readable", res.Error);
        // Full lock backoff is 50+100+200 = 350 ms; a missing file must stay well below that (load-tolerant bound).
        Assert.True(sw.ElapsedMilliseconds < 300, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void File_replaced_by_rename_is_fine_and_the_reader_never_writes()
    {
        // Hive compaction: write .hivec, rename over .hive. We never hold the file open (read fully, then close).
        using var dir = new HiveTempDir();
        var path = dir.Write("a.hive", new HiveTestWriter().Put("k", W.Str("old")).ToArray());
        var compact = dir.Write("a.hivec", new HiveTestWriter().Put("k", W.Str("new")).ToArray());
        var before = File.GetLastWriteTimeUtc(path);
        Assert.Equal("old", new HiveReader().Read(path, All)["k"]);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        File.Move(compact, path, overwrite: true);
        Assert.Equal("new", new HiveReader().Read(path, All)["k"]);
    }

    [Fact]
    public void Oversized_file_is_refused_instead_of_buffered()
    {
        using var dir = new HiveTempDir();
        var path = dir.File("big.hive");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            fs.SetLength(HiveReader.MaxFileBytes + 1);
        var before = GC.GetTotalMemory(false);
        var res = new HiveReader().ReadDetailed(path, All);
        Assert.Equal("file not readable", res.Error);
        Assert.True(GC.GetTotalMemory(false) - before < 64L * 1024 * 1024);
    }
}

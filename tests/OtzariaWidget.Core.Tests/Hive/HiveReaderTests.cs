using System.Diagnostics;
using System.Numerics;
using System.Text;
using OtzariaWidget.Core.Hive;
using Xunit.Abstractions;
using W = OtzariaWidget.Core.Tests.Hive.HiveTestWriter;

namespace OtzariaWidget.Core.Tests.Hive;

public class HiveReaderTests(ITestOutputHelper output)
{
    private static readonly Func<string, bool> All = _ => true;

    private static HiveParseResult Parse(HiveTestWriter w, Func<string, bool>? f = null)
        => HiveParser.Parse(w.ToArray(), f ?? All);

    [Fact]
    public void Empty_file_gives_empty_clean_result()
    {
        using var dir = new HiveTempDir();
        var path = dir.Write("e.hive", []);
        var res = new HiveReader().ReadDetailed(path);
        Assert.Empty(res.Values);
        Assert.True(res.IsClean);
        Assert.Equal(0, res.FrameCount);
    }

    [Fact]
    public void Missing_file_returns_empty_without_throwing()
    {
        using var dir = new HiveTempDir();
        var values = new HiveReader().Read(dir.File("nope.hive"), All);
        Assert.Empty(values);
    }

    [Fact]
    public void Scalar_types_roundtrip()
    {
        var w = new HiveTestWriter()
            .Put("null", W.Null())
            .Put("int", W.Int(-1234567))
            .Put("intBig", W.Int(1L << 52))
            .Put("double", W.Double(25.5))
            .Put("true", W.Bool(true))
            .Put("false", W.Bool(false))
            .Put("str", W.Str("שלום עולם ✓"))
            .Put("emptyStr", W.Str(""))
            .Put("bytes", W.Bytes([1, 2, 3, 255]));
        var res = Parse(w);
        var v = res.Values;
        Assert.True(res.IsClean);
        Assert.Equal(9, res.FrameCount);
        Assert.True(v.ContainsKey("null"));
        Assert.Null(v["null"]);
        Assert.Equal(-1234567L, Assert.IsType<long>(v["int"]));
        Assert.Equal(1L << 52, v["intBig"]);
        Assert.Equal(25.5, Assert.IsType<double>(v["double"]));
        Assert.Equal(true, v["true"]);
        Assert.Equal(false, v["false"]);
        Assert.Equal("שלום עולם ✓", v["str"]);
        Assert.Equal("", v["emptyStr"]);
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, v["bytes"]);
    }

    [Fact]
    public void Int_stored_as_double_truncates_toward_zero()
    {
        var w = new HiveTestWriter()
            .Put("a", W.IntFromRawDouble(3.99))
            .Put("b", W.IntFromRawDouble(-3.99));
        var v = Parse(w).Values;
        Assert.Equal(3L, v["a"]);
        Assert.Equal(-3L, v["b"]);
    }

    [Fact]
    public void List_and_set_types()
    {
        var w = new HiveTestWriter()
            .Put("il", W.IntList(1, -2, 3))
            .Put("dl", W.DoubleList(1.5, 2.5))
            .Put("bl", W.BoolList(true, false, true))
            .Put("sl", W.StrList("a", "בג", ""))
            .Put("is", W.IntSet(7, 8))
            .Put("ds", W.DoubleSet(0.5))
            .Put("ss", W.StrSet("x", "y"))
            .Put("dyn", W.List(W.Int(1), W.Str("two"), W.Bool(true), W.Null(), W.List(W.Int(9))))
            .Put("dynset", W.Set(W.Str("q"), W.Int(5)));
        var v = Parse(w).Values;
        Assert.Equal(new object?[] { 1L, -2L, 3L }, (List<object?>)v["il"]!);
        Assert.Equal(new object?[] { 1.5, 2.5 }, (List<object?>)v["dl"]!);
        Assert.Equal(new object?[] { true, false, true }, (List<object?>)v["bl"]!);
        Assert.Equal(new object?[] { "a", "בג", "" }, (List<object?>)v["sl"]!);
        Assert.Equal(new object?[] { 7L, 8L }, (List<object?>)v["is"]!);
        Assert.Equal(new object?[] { 0.5 }, (List<object?>)v["ds"]!);
        Assert.Equal(new object?[] { "x", "y" }, (List<object?>)v["ss"]!);
        var dyn = (List<object?>)v["dyn"]!;
        Assert.Equal(5, dyn.Count);
        Assert.Equal(1L, dyn[0]);
        Assert.Equal("two", dyn[1]);
        Assert.Null(dyn[3]);
        Assert.Equal(new object?[] { 9L }, (List<object?>)dyn[4]!);
        Assert.Equal(new object?[] { "q", 5L }, (List<object?>)v["dynset"]!);
    }

    [Fact]
    public void Map_hivelist_and_special_types()
    {
        var w = new HiveTestWriter()
            .Put("map", W.Map((W.Str("a"), W.Int(1)), (W.Int(2), W.List(W.Str("z"))), (W.Str("n"), W.Null())))
            .Put("hl", W.HiveList("books", "k1", "k2"))
            .Put("dt", W.DateTimeMs(1_700_000_000_123))
            .Put("big", W.BigInt("12345678901234567890123"))
            .Put("bigSmall", W.BigInt("-42"))
            .Put("dtTzUtc", W.DateTimeTz(1_700_000_000_000.5, true))
            .Put("dtTzLocal", W.DateTimeTz(1_700_000_000_000, false))
            .Put("dur", W.Duration(90_000));
        var v = Parse(w).Values;

        var map = Assert.IsType<Dictionary<object, object?>>(v["map"]);
        Assert.Equal(1L, map["a"]);
        Assert.Equal(new object?[] { "z" }, (List<object?>)map[2L]!);
        Assert.Null(map["n"]);

        Assert.Equal(new object?[] { "k1", "k2" }, (List<object?>)v["hl"]!);

        var dt = Assert.IsType<DateTime>(v["dt"]);
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123).UtcDateTime, dt);

        Assert.Equal(BigInteger.Parse("12345678901234567890123"), Assert.IsType<BigInteger>(v["big"]));
        Assert.Equal(-42L, v["bigSmall"]);

        var utc = Assert.IsType<DateTime>(v["dtTzUtc"]);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(DateTime.UnixEpoch.AddMilliseconds(1_700_000_000_000).AddTicks(5000), utc);
        var local = Assert.IsType<DateTime>(v["dtTzLocal"]);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000).UtcDateTime, local.ToUniversalTime());

        Assert.Equal(TimeSpan.FromSeconds(90), v["dur"]);
    }

    [Fact]
    public void Extension_type_id_wrapping_a_builtin_type_is_decoded()
    {
        // type id 21 followed by u16 real id 4 (String) must behave like a plain string
        var payload = new List<byte>();
        payload.AddRange(BitConverter.GetBytes((uint)3));
        payload.AddRange(Encoding.UTF8.GetBytes("abc"));
        var w = new HiveTestWriter().Put("s", W.Ext(4, payload.ToArray()));
        Assert.Equal("abc", Parse(w).Values["s"]);
    }

    [Fact]
    public void Unknown_custom_adapter_is_skipped_and_parsing_continues()
    {
        var w = new HiveTestWriter()
            .Put("before", W.Str("1"))
            .Put("custom", W.Ext(300, 1, 2, 3, 4, 5))
            .Put("after", W.Str("2"));
        var res = Parse(w);
        Assert.True(res.IsClean);
        Assert.Equal(3, res.FrameCount);
        Assert.Equal(1, res.SkippedValueCount);
        Assert.Equal("1", res.Values["before"]);
        Assert.Equal("2", res.Values["after"]);
        Assert.False(res.Values.ContainsKey("custom"));
    }

    [Fact]
    public void Custom_adapter_overwriting_a_key_drops_the_stale_value()
    {
        var w = new HiveTestWriter()
            .Put("k", W.Str("old"))
            .Put("k", W.Ext(40, 9, 9));
        Assert.False(Parse(w).Values.ContainsKey("k"));
    }

    [Fact]
    public void String_and_int_keys_are_supported()
    {
        var w = new HiveTestWriter()
            .Put("name", W.Str("v"))
            .Put(42u, W.Str("int-key"))
            .Put(0xFFFFFFFFu, W.Bool(true))
            .Put("עברית", W.Int(5));
        var v = Parse(w).Values;
        Assert.Equal("v", v["name"]);
        Assert.Equal("int-key", v["42"]);
        Assert.Equal(true, v["4294967295"]);
        Assert.Equal(5L, v["עברית"]);
    }

    [Fact]
    public void Last_frame_wins_and_tombstone_removes()
    {
        var w = new HiveTestWriter()
            .Put("a", W.Int(1))
            .Put("b", W.Str("keep"))
            .Put("a", W.Int(2))
            .Put("c", W.Str("gone"))
            .Delete("c")
            .Put("d", W.Str("x"))
            .Delete("d")
            .Put("d", W.Str("back"))
            .Delete("never-existed");
        var res = Parse(w);
        Assert.Equal(9, res.FrameCount);
        Assert.Equal(3, res.TombstoneCount);
        Assert.Equal(2L, res.Values["a"]);
        Assert.Equal("keep", res.Values["b"]);
        Assert.False(res.Values.ContainsKey("c"));
        Assert.Equal("back", res.Values["d"]);
        Assert.Equal(3, res.Values.Count);
    }

    [Fact]
    public void Bad_crc_mid_file_stops_parsing_and_keeps_earlier_frames()
    {
        var f1 = HiveTestWriter.Frame(W.KeyStr("one"), W.Str("1"));
        var f2 = HiveTestWriter.Frame(W.KeyStr("two"), W.Str("2"));
        var f3 = HiveTestWriter.Frame(W.KeyStr("three"), W.Str("3"));
        f2[^1] ^= 0xFF; // corrupt crc
        var res = HiveParser.Parse([.. f1, .. f2, .. f3], All);
        Assert.False(res.IsClean);
        Assert.Equal("CRC mismatch", res.Error);
        Assert.Equal(1, res.FrameCount);
        Assert.Equal(f1.Length, res.ParsedBytes);
        Assert.Equal("1", res.Values["one"]);
        Assert.False(res.Values.ContainsKey("two"));
        Assert.False(res.Values.ContainsKey("three"));
    }

    [Fact]
    public void Corrupted_payload_byte_is_detected_by_crc()
    {
        var f1 = HiveTestWriter.Frame(W.KeyStr("one"), W.Str("hello"));
        f1[10] ^= 0x01;
        var res = HiveParser.Parse(f1, All);
        Assert.Empty(res.Values);
        Assert.False(res.IsClean);
    }

    [Fact]
    public void Torn_tail_is_ignored_for_every_truncation_point()
    {
        var f1 = HiveTestWriter.Frame(W.KeyStr("one"), W.Str("1"));
        var f2 = HiveTestWriter.Frame(W.KeyStr("two"), W.Str("2"));
        for (int cut = 1; cut < f2.Length; cut++)
        {
            var data = f1.Concat(f2.Take(cut)).ToArray();
            var res = HiveParser.Parse(data, All);
            Assert.Equal("1", res.Values["one"]);
            Assert.False(res.Values.ContainsKey("two"));
            Assert.Equal(f1.Length, res.ParsedBytes);
            Assert.False(res.IsClean);
        }
    }

    [Fact]
    public void Invalid_frame_length_stops_parsing()
    {
        var f1 = HiveTestWriter.Frame(W.KeyStr("one"), W.Str("1"));
        var junk = new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }; // length < 8
        var res = HiveParser.Parse([.. f1, .. junk], All);
        Assert.Equal("1", res.Values["one"]);
        Assert.False(res.IsClean);
    }

    [Fact]
    public void Absurd_list_count_does_not_throw_or_allocate()
    {
        // CRC-valid frame whose list claims 4 billion elements
        var payload = new List<byte> { 10 };
        payload.AddRange(BitConverter.GetBytes(0xFFFFFFF0u));
        var w = new HiveTestWriter().Put("bad", payload.ToArray()).Put("ok", W.Str("y"));
        var res = Parse(w);
        Assert.False(res.Values.ContainsKey("bad"));
        Assert.Equal("y", res.Values["ok"]);
    }

    [Fact]
    public void Deeply_nested_list_is_rejected_gracefully()
    {
        byte[] v = W.Int(1);
        for (int i = 0; i < 200; i++) v = W.List(v);
        var w = new HiveTestWriter().Put("deep", v).Put("ok", W.Str("y"));
        var res = Parse(w);
        Assert.False(res.Values.ContainsKey("deep"));
        Assert.Equal("y", res.Values["ok"]);
    }

    [Fact]
    public void Key_filter_prevents_materializing_other_keys()
    {
        var w = new HiveTestWriter()
            .Put("key-google-calendar-client-secret", W.Str("TOPSECRET"))
            .Put("key-dark-mode", W.Bool(true))
            .Put("sz:tracked_books", W.Str("[1]"));
        var asked = new List<string>();
        var res = Parse(w, k => { asked.Add(k); return k.StartsWith("sz:") || k == "key-dark-mode"; });
        Assert.Equal(2, res.Values.Count);
        Assert.False(res.Values.ContainsKey("key-google-calendar-client-secret"));
        Assert.DoesNotContain(res.Values.Values, x => x is string s && s.Contains("TOPSECRET"));
        Assert.Equal(3, asked.Count); // all frames were still walked
    }

    [Fact]
    public void Filtered_out_key_with_garbage_value_is_harmless()
    {
        var w = new HiveTestWriter()
            .Put("junk", W.Ext(999, 1, 2, 3))
            .Put("good", W.Str("g"));
        var res = Parse(w, k => k == "good");
        Assert.Equal(0, res.SkippedValueCount);
        Assert.Equal("g", res.Values["good"]);
    }

    [Fact]
    public void Reading_from_file_works_while_another_handle_holds_it_open_for_writing()
    {
        using var dir = new HiveTempDir();
        var path = dir.File("a.hive");
        var w = new HiveTestWriter().Put("k", W.Str("v"));
        using var writerHandle = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writerHandle.Write(w.ToArray());
        writerHandle.Flush();
        var values = new HiveReader().Read(path, All);
        Assert.Equal("v", values["k"]);
    }

    [Fact]
    public void Large_value_and_many_frames()
    {
        var big = new string('א', 200_000);
        var w = new HiveTestWriter();
        for (int i = 0; i < 2000; i++) w.Put("k" + i, W.Int(i));
        w.Put("big", W.Str(big));
        var res = Parse(w);
        Assert.True(res.IsClean);
        Assert.Equal(2001, res.Values.Count);
        Assert.Equal(1999L, res.Values["k1999"]);
        Assert.Equal(big, res.Values["big"]);
    }

    [Fact]
    public void Python_reference_parser_agrees_on_a_synthetic_file()
    {
        var script = FindUp("tools", "hive_dump.py");
        if (script is null) return;
        var w = new HiveTestWriter()
            .Put("s", W.Str("שלום"))
            .Put("i", W.Int(-77))
            .Put("b", W.Bool(true))
            .Put("over", W.Int(1))
            .Put("over", W.Str("final"))
            .Put("dead", W.Str("x"))
            .Delete("dead")
            .Put("list", W.StrList("p", "q"));
        using var dir = new HiveTempDir();
        var path = dir.Write("py.hive", w.ToArray());

        string? stdout = RunPython(script, path);
        if (stdout is null) return; // python not available
        int nl = stdout.IndexOf('\n');
        var header = stdout[..nl];
        Assert.Contains("clean", header);
        using var doc = System.Text.Json.JsonDocument.Parse(stdout[nl..]);
        var py = doc.RootElement;

        var ours = Parse(w).Values;
        // python raises on the custom adapter value and would stop; hive_dump's value() raises ValueError for 'custom'.
        // So compare only when python produced the full set; otherwise compare the keys it did get.
        foreach (var p in py.EnumerateObject())
        {
            Assert.True(ours.ContainsKey(p.Name), "missing key " + p.Name);
            switch (p.Value.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String: Assert.Equal(p.Value.GetString(), ours[p.Name]); break;
                case System.Text.Json.JsonValueKind.Number: Assert.Equal(p.Value.GetInt64(), ours[p.Name]); break;
                case System.Text.Json.JsonValueKind.True: Assert.Equal(true, ours[p.Name]); break;
                case System.Text.Json.JsonValueKind.Array:
                    Assert.Equal(p.Value.EnumerateArray().Select(e => e.GetString()).ToArray(),
                        ((List<object?>)ours[p.Name]!).Cast<string>().ToArray());
                    break;
            }
        }
        output.WriteLine($"python keys={py.EnumerateObject().Count()} ours={ours.Count}");
    }

    [Fact]
    public void Real_app_preferences_smoke_test_counts_only()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData)) return;
        var path = Path.Combine(appData, "otzaria", "app_preferences.hive");
        if (!File.Exists(path)) return;

        var res = new HiveReader().ReadDetailed(path);
        output.WriteLine($"bytes={res.TotalBytes} frames={res.FrameCount} tombstones={res.TombstoneCount} " +
                         $"skipped={res.SkippedValueCount} liveKeys={res.Values.Count} clean={res.IsClean}");
        Assert.True(res.FrameCount > 0);
        Assert.True(res.IsClean, "real file should parse cleanly: " + res.Error);
        Assert.Equal(res.TotalBytes, res.ParsedBytes);
        Assert.Equal(0, res.SkippedValueCount);
        Assert.True(res.Values.Count > 0);
        Assert.True(res.Values.TryGetValue("key-selected-city", out var city));
        var cityStr = Assert.IsType<string>(city);
        Assert.False(string.IsNullOrWhiteSpace(cityStr));
    }

    // ----- helpers ------------------------------------------------------------------------

    private static string? FindUp(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relative]);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? RunPython(string script, string hivePath)
    {
        foreach (var exe in new[] { "python", "python3", "py" })
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-I");
                psi.ArgumentList.Add("-X");
                psi.ArgumentList.Add("utf8");
                psi.ArgumentList.Add(script);
                psi.ArgumentList.Add(hivePath);
                psi.ArgumentList.Add("--json");
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                using var p = Process.Start(psi);
                if (p is null) continue;
                var outText = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                if (!p.WaitForExit(20000)) { p.Kill(); continue; }
                if (outText.StartsWith('#')) return outText;
            }
            catch (System.ComponentModel.Win32Exception) { /* try next */ }
        }
        return null;
    }
}

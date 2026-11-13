using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Calendar.Zmanim;

/// <summary>Computes Otzaria zmanim for a (city, civil date).</summary>
public static class ZmanimEngine
{
    /// <summary>
    /// Zmanim for the date in registry (Otzaria display) order, only those in <paramref name="ids"/> (unknown ids are ignored).
    /// Otzaria behaviour reproduced: a zman whose "only when" predicate is false that day (candle lighting, motzei Shabbat/YT,
    /// chametz times, fast ends) is OMITTED entirely; a relevant zman that cannot be computed (polar day/night, ...) is returned
    /// with <c>Time = null</c> (Otzaria hides it; the widget should show "--" or hide it).
    /// </summary>
    public static IReadOnlyList<ZmanTime> Compute(DateOnly date, CityInfo city, IReadOnlyList<string> ids)
    {
        // null list / null elements / unknown ids never throw (the widget feeds persisted user settings straight in)
        var wanted = new HashSet<string>(ids?.Where(i => i != null) ?? [], StringComparer.Ordinal);
        var result = new List<ZmanTime>(wanted.Count);
        if (wanted.Count == 0) return result;
        ZmanContext ctx;
        try { ctx = ZmanContext.Create(city, date); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException) { return result; } // date/zone outside the supported range
        foreach (var e in ZmanimRegistry.All)
        {
            if (!wanted.Contains(e.Id)) continue;
            if (!ZmanCalculator.IsRelevant(e.Id, ctx.Jewish)) continue;
            if (!ZmanCalculator.TryCompute(e.Id, ctx, out var v)) continue;
            string? display = null;
            if (v.Time != null) display = v.Label ?? ZmanTimeFormatter.Format(v.Time.Value);
            result.Add(new ZmanTime(e.Id, e.FullName, v.Time) { DisplayText = display });
        }
        return result;
    }
}

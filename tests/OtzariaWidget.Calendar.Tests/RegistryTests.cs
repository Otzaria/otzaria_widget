using System.Text.RegularExpressions;
using OtzariaWidget.Calendar.Cities;
using OtzariaWidget.Calendar.Zmanim;

namespace OtzariaWidget.Calendar.Tests;

public class RegistryTests
{
    private static readonly CalendarService Svc = new();

    private static readonly string[] ExpectedDefaults =
    [
        "alos72Degrees", "alos90Degrees", "misheyakir10point2", "sunrise", "sofZmanShmaMGA90Degrees", "sofZmanShmaGRA",
        "sofZmanTfilaMGA90Degrees", "sofZmanTfilaGRA", "chatzos", "chatzosLayla", "minchaGedolaGreater30", "minchaKetanaGRA", "plagGRA",
        "sunset", "seaLevelSunset", "tzeitTikochinsky", "tzeitItimLeVina", "rt72Shavos", "candleLighting", "motzeiShabbos",
        "motzeiShabbosChazonIsh", "motzeiYomTov", "motzeiYomTovChazonIsh", "achilasChametzMGADegrees", "achilasChametzGRA",
        "biurChametzMGADegrees", "biurChametzGRA", "fastEndTikLenient", "fastEndItimLenient", "fastEndTikStringent", "fastEndItimStringent",
    ];

    [Fact]
    public void Registry_has_98_unique_ids_in_otzaria_order()
    {
        var r = Svc.ZmanimRegistry;
        Assert.Equal(98, r.Count);
        Assert.Equal(98, r.Select(z => z.Id).Distinct().Count());
        Assert.Equal("alos72Degrees", r[0].Id);
        Assert.Equal("alos90Degrees", r[1].Id);
        Assert.Equal("sunset", r[53].Id);      // #54 in the research table
        Assert.Equal("candleLighting", r[79].Id); // #80
        Assert.Equal("sofKidushLevana15", r[^1].Id);
    }

    [Fact]
    public void Default_enabled_set_is_the_31_otzaria_defaults()
    {
        Assert.Equal(31, Svc.DefaultEnabledZmanim.Count);
        Assert.Equal(ExpectedDefaults, Svc.DefaultEnabledZmanim.ToArray());
        Assert.All(Svc.DefaultEnabledZmanim, id => Assert.Contains(Svc.ZmanimRegistry, z => z.Id == id));
        Assert.Equal(31, Svc.ZmanimRegistryDetails.Count(e => e.DefaultEnabled));
    }

    [Fact]
    public void Every_entry_has_a_hebrew_title_and_description()
    {
        var hebrew = new Regex("[א-ת]");
        foreach (var z in Svc.ZmanimRegistry)
        {
            Assert.Matches(hebrew, z.Title);
            Assert.False(string.IsNullOrWhiteSpace(z.Description), z.Id);
            Assert.Matches(hebrew, z.Description!);
        }
        foreach (var e in Svc.ZmanimRegistryDetails)
        {
            Assert.Matches(hebrew, e.Title);
            Assert.Matches(hebrew, e.Category);
        }
    }

    [Theory]
    [InlineData("alos72Degrees", "עלות השחר — 72 דק׳ (מעלות)")]
    [InlineData("sunrise", "הנץ הנראה")]
    [InlineData("sunriseAstronomical", "נץ אסטרונומי — בתיקון גובה")]
    [InlineData("sofZmanShmaGRA", "סוף זמן ק\"ש — גר\"א")]
    [InlineData("chatzos", "חצות היום")]
    [InlineData("chatzosLayla", "חצות לילה")]
    [InlineData("sunset", "שקיעה")]
    [InlineData("seaLevelSunset", "שקיעה מישורית")]
    [InlineData("tzeitTikochinsky", "צאת הכוכבים — טיקוצ׳ינסקי")]
    [InlineData("rt72Shavos", "רבנו תם — 72 דק׳ (שוות)")]
    [InlineData("candleLighting", "הדלקת נרות")]
    [InlineData("motzeiShabbosChazonIsh", "מוצאי שבת — חזו\"א")]
    [InlineData("fastEndItimStringent", "יציאת הצום — לחומרא — עיתים לבינה")]
    [InlineData("sofKidushLevana15", "סוף קידוש לבנה — 15 ימים")]
    public void Titles_are_otzarias_full_names(string id, string title)
        => Assert.Equal(title, Svc.ZmanimRegistry.Single(z => z.Id == id).Title);

    [Fact]
    public void Details_expose_subtitle_category_and_pairing()
    {
        var alos = Svc.ZmanimRegistryDetails.Single(e => e.Id == "alos72Degrees");
        Assert.Equal("עלות השחר", alos.Title);
        Assert.Equal("72 דק׳ (מעלות)", alos.Subtitle);
        Assert.Equal("עלות השחר", alos.Category);
        Assert.Equal("alosDeg", alos.PairId);
        Assert.True(Svc.ZmanimRegistryDetails.Single(e => e.Id == "tchilasKidushLevana3").ShowHebrewDate);
        Assert.False(Svc.ZmanimRegistryDetails.Single(e => e.Id == "sunset").ShowHebrewDate);
    }

    [Fact]
    public void Every_registry_id_has_a_calculation()
    {
        var jer = Svc.FindCity("ירושלים")!;
        var ctx = ZmanContext.Create(jer, new DateOnly(2026, 11, 13));
        foreach (var z in Svc.ZmanimRegistry)
            Assert.True(ZmanCalculator.TryCompute(z.Id, ctx, out _), z.Id);
    }

    [Fact]
    public void Every_zman_computes_for_every_city_on_a_regular_day_with_the_city_offset()
    {
        // 2026-03-04 (Wed): none of the day-dependent predicates apply except always-relevant ones; Dublin..Sydney included.
        var date = new DateOnly(2026, 3, 4);
        var ids = Svc.ZmanimRegistry.Select(z => z.Id).ToList();
        foreach (var city in Svc.Cities)
        {
            var zone = TimeZones.Resolve(city);
            var result = Svc.GetZmanim(date, city, ids);
            Assert.Contains(result, z => z.Id == "sunset" && z.Time != null);
            foreach (var z in result.Where(z => z.Time != null))
                Assert.Equal(zone.GetUtcOffset(z.Time!.Value), z.Time!.Value.Offset);
        }
    }
}

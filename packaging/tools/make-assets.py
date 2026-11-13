"""
Generates all PNG/ICO assets for the Otzaria Widget package (needs Pillow built with raqm for Hebrew shaping).

  python packaging\tools\make-assets.py

Outputs
  packaging\Assets\*.png                         package logos (Store / Start tile)
  packaging\ProviderAssets\Icon*.png             provider + per-widget picker icons
  packaging\ProviderAssets\Screenshot_*.png      300x304 picker screenshots (light + dark)
  src\OtzariaWidget.App\Assets\app.ico (+png)    exe icon

Palette: Otzaria brown #2C1B02, gold #D4A22A.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PKG = os.path.abspath(os.path.join(HERE, ".."))
ROOT = os.path.abspath(os.path.join(PKG, ".."))
ASSETS = os.path.join(PKG, "Assets")
PROV = os.path.join(PKG, "ProviderAssets")
APPASSETS = os.path.join(ROOT, "src", "OtzariaWidget.App", "Assets")
for d in (ASSETS, PROV, APPASSETS):
    os.makedirs(d, exist_ok=True)

BROWN = (44, 27, 2, 255)
BROWN2 = (70, 44, 6, 255)
GOLD = (212, 162, 42, 255)
GOLD_L = (240, 198, 90, 255)
CREAM = (244, 231, 197, 255)
INK = (150, 110, 30, 255)
CLEAR = (0, 0, 0, 0)
SS = 4  # supersampling

FONT_R = r"C:\Windows\Fonts\segoeui.ttf"
FONT_B = r"C:\Windows\Fonts\segoeuib.ttf"


def font(sz, bold=False):
    for p in ((FONT_B if bold else FONT_R), r"C:\Windows\Fonts\arial.ttf"):
        try:
            return ImageFont.truetype(p, int(sz))
        except Exception:
            pass
    return ImageFont.load_default()


class Scaled:
    """Draw proxy: glyph functions use a 100x100 unit canvas, K is the supersampled pixels per unit."""

    def __init__(self, d, k):
        self.d, self.k = d, k

    def polygon(self, pts, fill=None):
        self.d.polygon([(x * self.k, y * self.k) for x, y in pts], fill=fill)

    def line(self, pts, fill=None, width=1):
        self.d.line([(x * self.k, y * self.k) for x, y in pts], fill=fill, width=max(1, int(width * self.k)))

    def rectangle(self, box, fill=None):
        self.d.rectangle([v * self.k for v in box], fill=fill)

    def rounded_rectangle(self, box, radius=0, fill=None):
        self.d.rounded_rectangle([v * self.k for v in box], radius=radius * self.k, fill=fill)

    def ellipse(self, box, fill=None):
        self.d.ellipse([v * self.k for v in box], fill=fill)

    def arc(self, box, a, b, fill=None, width=1):
        self.d.arc([v * self.k for v in box], a, b, fill=fill, width=int(width * self.k))

    def pieslice(self, box, a, b, fill=None):
        self.d.pieslice([v * self.k for v in box], a, b, fill=fill)

    def text(self, xy, t, size=40, fill=None, bold=True):
        self.d.text((xy[0] * self.k, xy[1] * self.k), t, font=font(size * self.k, bold), fill=fill, anchor="mm", direction="rtl")


# --------------------------------------------------------------------------- glyphs (100x100 unit canvas)
def glyph_book(d):
    d.polygon([(50, 30), (50, 78), (14, 72), (14, 24)], fill=CREAM)
    d.polygon([(50, 30), (50, 78), (86, 72), (86, 24)], fill=(232, 213, 160, 255))
    d.line([(50, 28), (50, 80)], fill=GOLD, width=4)
    for i in range(4):
        y = 34 + i * 9
        d.line([(20, y - 1 + i), (44, y + 3 + i)], fill=INK, width=2)
        d.line([(56, y + 3 + i), (80, y - 1 + i)], fill=INK, width=2)
    d.rectangle([12, 76, 88, 80], fill=GOLD)


def glyph_bookmark(d):
    d.rounded_rectangle([24, 16, 76, 86], radius=4, fill=CREAM)
    d.polygon([(52, 16), (68, 16), (68, 54), (60, 46), (52, 54)], fill=GOLD)
    for i in range(3):
        d.line([(32, 62 + i * 8), (68, 62 + i * 8)], fill=INK, width=2)


def glyph_ring(d):
    box = [16, 16, 84, 84]
    d.arc(box, 0, 360, fill=BROWN2, width=12)
    d.arc(box, -90, 160, fill=GOLD_L, width=12)
    d.ellipse([40, 40, 60, 60], fill=CREAM)


def glyph_sun(d):
    cx, cy = 50, 60
    d.pieslice([26, 36, 74, 84], 180, 360, fill=GOLD_L)
    for a in range(200, 345, 28):
        x1 = cx + 30 * math.cos(math.radians(a)); y1 = cy + 30 * math.sin(math.radians(a))
        x2 = cx + 42 * math.cos(math.radians(a)); y2 = cy + 42 * math.sin(math.radians(a))
        d.line([(x1, y1), (x2, y2)], fill=GOLD, width=4)
    d.rectangle([14, 60, 86, 64], fill=CREAM)
    d.rectangle([24, 70, 76, 73], fill=INK)


def glyph_calendar(d):
    d.rounded_rectangle([16, 20, 84, 84], radius=8, fill=CREAM)
    d.rounded_rectangle([16, 20, 84, 38], radius=8, fill=GOLD)
    d.rectangle([16, 30, 84, 38], fill=GOLD)
    d.rectangle([30, 12, 35, 26], fill=BROWN)
    d.rectangle([65, 12, 70, 26], fill=BROWN)
    d.text((50, 61), "\u05d0", size=40, fill=BROWN)


def glyph_dashboard(d):
    for (x, y, c) in ((16, 16, CREAM), (54, 16, GOLD_L), (16, 54, GOLD_L), (54, 54, CREAM)):
        d.rounded_rectangle([x, y, x + 30, y + 30], radius=6, fill=c)
    d.line([(22, 62), (40, 62)], fill=BROWN, width=3)
    d.line([(60, 24), (78, 24)], fill=BROWN, width=3)


GLYPHS = {
    "RecentBooks": glyph_book, "Bookmarks": glyph_bookmark, "ShamorZachor": glyph_ring,
    "Zmanim": glyph_sun, "HebrewCalendar": glyph_calendar, "Dashboard": glyph_dashboard, "App": glyph_book,
}


def icon(kind, size, bg=True):
    big = size * SS
    im = Image.new("RGBA", (big, big), CLEAR)
    d = ImageDraw.Draw(im)
    if bg:
        r = int(big * 0.22)
        d.rounded_rectangle([0, 0, big - 1, big - 1], radius=r, fill=BROWN)
        m = int(big * 0.035)
        d.rounded_rectangle([m, m, big - 1 - m, big - 1 - m], radius=r - m, outline=(212, 162, 42, 90), width=max(1, big // 90))
    inner = int(big * (0.74 if bg else 1.0))
    g = Image.new("RGBA", (inner, inner), CLEAR)
    GLYPHS[kind](Scaled(ImageDraw.Draw(g), inner / 100.0))
    im.alpha_composite(g, ((big - inner) // 2, (big - inner) // 2))
    return im.resize((size, size), Image.LANCZOS)


def wide_logo(w, h):
    im = Image.new("RGBA", (w * SS, h * SS), CLEAR)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, w * SS - 1, h * SS - 1], radius=int(h * SS * 0.12), fill=BROWN)
    gs = int(h * 0.78)
    g = icon("App", gs, bg=False).resize((gs * SS, gs * SS), Image.LANCZOS)
    im.alpha_composite(g, (int(w * SS * 0.07), (h * SS - g.height) // 2))
    d.text((int(w * SS * 0.93), int(h * SS * 0.40)), "\u05d0\u05d5\u05e6\u05e8\u05d9\u05d0", font=font(h * 0.19 * SS, True), fill=GOLD_L, anchor="rm", direction="rtl")
    d.text((int(w * SS * 0.93), int(h * SS * 0.64)), "\u05d5\u05d5\u05d9\u05d3\u05d2\u05f3\u05d8", font=font(h * 0.12 * SS), fill=CREAM, anchor="rm", direction="rtl")
    return im.resize((w, h), Image.LANCZOS)


# --------------------------------------------------------------------------- package + exe assets
for name, size in (("StoreLogo.png", 50), ("Square44x44Logo.png", 44), ("Square44x44Logo.scale-200.png", 88),
                   ("Square44x44Logo.targetsize-24_altform-unplated.png", 24),
                   ("Square44x44Logo.targetsize-48_altform-unplated.png", 48),
                   ("Square150x150Logo.png", 150), ("Square150x150Logo.scale-200.png", 300)):
    icon("App", size).save(os.path.join(ASSETS, name))
wide_logo(310, 150).save(os.path.join(ASSETS, "Wide310x150Logo.png"))
wide_logo(620, 300).save(os.path.join(ASSETS, "Wide310x150Logo.scale-200.png"))

base = icon("App", 256)
base.save(os.path.join(APPASSETS, "app.png"))
base.save(os.path.join(APPASSETS, "app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

icon("App", 64).save(os.path.join(PROV, "Icon.png"))
for kind in ("RecentBooks", "Bookmarks", "ShamorZachor", "Zmanim", "HebrewCalendar", "Dashboard"):
    icon(kind, 64).save(os.path.join(PROV, "Icon_%s.png" % kind))


# --------------------------------------------------------------------------- picker screenshots (300x304)
class Theme:
    def __init__(self, dark):
        self.bg = (32, 32, 32, 255) if dark else (249, 249, 249, 255)
        self.panel = (48, 48, 48, 255) if dark else (236, 236, 236, 255)
        self.fg = (255, 255, 255, 255) if dark else (28, 28, 28, 255)
        self.sub = (170, 170, 170, 255) if dark else (100, 100, 100, 255)
        self.accent = (240, 198, 90, 255) if dark else (150, 98, 0, 255)
        self.track = (70, 70, 70, 255) if dark else (214, 214, 214, 255)
        self.sep = (62, 62, 62, 255) if dark else (224, 224, 224, 255)


W, H = 300, 304
RM = W - 20  # right margin (RTL: content hugs the right edge)


def canvas(t):
    im = Image.new("RGBA", (W * SS, H * SS), CLEAR)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, W * SS - 1, H * SS - 1], radius=14 * SS, fill=t.bg)
    return im, d


def rtl(d, x, y, text, sz, fill, bold=False):
    d.text((x * SS, y * SS), text, font=font(sz * SS, bold), fill=fill, anchor="ra", direction="rtl")


def left(d, x, y, text, sz, fill, bold=False):
    d.text((x * SS, y * SS), text, font=font(sz * SS, bold), fill=fill, anchor="la", direction="ltr")


def bar(d, t, y, ratio, x0=20, x1=RM, h=6):
    d.rounded_rectangle([x0 * SS, y * SS, x1 * SS, (y + h) * SS], radius=h * SS // 2, fill=t.track)
    w = (x1 - x0) * ratio
    d.rounded_rectangle([(x1 - w) * SS, y * SS, x1 * SS, (y + h) * SS], radius=h * SS // 2, fill=t.accent)


def sep(d, t, y):
    d.line([(20 * SS, y * SS), (RM * SS, y * SS)], fill=t.sep, width=SS)


def header(d, t, title, sub=None):
    rtl(d, RM, 16, title, 18, t.fg, True)
    if sub:
        rtl(d, RM, 42, sub, 12, t.sub)


def shot_recent(d, t):
    header(d, t, "ספרים אחרונים")
    d.rounded_rectangle([14 * SS, 52 * SS, (W - 14) * SS, 116 * SS], radius=8 * SS, fill=t.panel)
    rtl(d, RM, 60, "המשך קריאה", 11, t.sub)
    rtl(d, RM, 76, "ברכות", 17, t.fg, True)
    rtl(d, RM, 98, "דף ב עמוד א", 12, t.sub)
    y = 128
    for title, ref, when in (("שולחן ערוך אורח חיים", "סימן א סעיף ב", "אתמול"), ("משנה ברורה", "סימן רמב ס״ק ג", "לפני 3 ימים"),
                             ("רמב״ם הלכות שבת", "פרק ה הלכה ג", "9 נוב׳"), ("ספר החינוך", "מצווה לב", "5 נוב׳")):
        sep(d, t, y - 4)
        rtl(d, RM, y, title, 14, t.fg)
        rtl(d, RM, y + 19, ref, 11, t.sub)
        left(d, 20, y + 3, when, 11, t.sub)
        y += 42


def shot_bookmarks(d, t):
    header(d, t, "סימניות")
    y = 54
    for label, sub in (("הלכות תפילה", "שולחן ערוך אורח חיים"), ("יסודות", "רמב״ם הלכות שבת"), ("אהבת ישראל", "תניא ליקוטי אמרים"),
                       ("סימן שכ", "משנה ברורה"), ("דף י עמוד ב", "ברכות"), ("פרק ג", "מסילת ישרים")):
        sep(d, t, y - 5)
        rtl(d, RM, y, label, 15, t.fg)
        rtl(d, RM, y + 21, sub, 11, t.sub)
        y += 41


def shot_sz(d, t):
    header(d, t, "שמור וזכור", "בתהליך")
    y = 66
    for title, pct, r in (("ברכות", "45%", 0.45), ("רמב״ם הלכות שבת", "80%", 0.80), ("חובות הלבבות", "33%", 0.33), ("שולחן ערוך אורח חיים", "12%", 0.12)):
        sep(d, t, y - 6)
        rtl(d, RM, y, title, 15, t.fg)
        left(d, 20, y, pct, 15, t.accent, True)
        bar(d, t, y + 26, r)
        rtl(d, RM, y + 38, "התחלה מצוינת!" if r < .3 else "חצי הדרך מאחוריך!", 11, t.sub)
        y += 58


def shot_zmanim(d, t):
    header(d, t, "זמני היום", "ירושלים · ג׳ בכסלו תשפ״ז")
    rows = [("עלות השחר", "04:52", "past"), ("הנץ החמה", "06:17", "past"), ("סוף זמן ק״ש", "09:31", "past"),
            ("חצות היום", "11:37", "next"), ("מנחה גדולה", "12:08", ""), ("פלג המנחה", "15:53", ""), ("שקיעה", "16:37", ""), ("צאת הכוכבים", "17:15", "")]
    y = 70
    for name, tm, st in rows:
        col = t.sub if st == "past" else (t.accent if st == "next" else t.fg)
        b = st == "next"
        rtl(d, RM, y, name, 15, col, b)
        left(d, 20, y, tm, 15, col, b)
        y += 28


def shot_calendar(d, t):
    rtl(d, RM, 16, "לוח עברי", 12, t.sub)
    rtl(d, RM, 36, "ג׳ בכסלו תשפ״ז", 26, t.accent, True)
    rtl(d, RM, 78, "יום שישי · 13 בנובמבר 2026", 13, t.sub)
    sep(d, t, 108)
    y = 120
    for label, text in (("", "ערב שבת"), ("הדלקת נרות", "15:57"), ("פרשה", "ויצא"), ("דף יומי", "בבא קמא ד"), ("ירושלמי", "שבת ה")):
        if label:
            rtl(d, RM, y + 2, label, 11, t.sub)
            rtl(d, RM - 80, y, text, 15, t.fg)
        else:
            rtl(d, RM, y, text, 15, t.accent, True)
        y += 30


def shot_dashboard(d, t):
    header(d, t, "לוח אישי")
    y = 52
    for title, text, meta in (("התאריך היום", "ג׳ בכסלו תשפ״ז", ""), ("הזמן הבא", "חצות היום", "11:37"), ("המשך קריאה", "ברכות", ""),
                              ("שמור וזכור", "ברכות", "45%"), ("סימניות", "הלכות תפילה", "")):
        sep(d, t, y - 4)
        rtl(d, RM, y, title, 10, t.sub)
        rtl(d, RM, y + 13, text, 15, t.fg, title == "התאריך היום")
        if meta:
            left(d, 20, y + 14, meta, 14, t.accent, True)
        if title == "שמור וזכור":
            bar(d, t, y + 38, 0.45)
        y += 52 if title == "שמור וזכור" else 44


SHOTS = {"RecentBooks": shot_recent, "Bookmarks": shot_bookmarks, "ShamorZachor": shot_sz,
         "Zmanim": shot_zmanim, "HebrewCalendar": shot_calendar, "Dashboard": shot_dashboard}

for kind, fn in SHOTS.items():
    for dark in (False, True):
        t = Theme(dark)
        im, d = canvas(t)
        fn(d, t)
        im = im.resize((W, H), Image.LANCZOS)
        im.save(os.path.join(PROV, "Screenshot_%s%s.png" % (kind, "_dark" if dark else "")))

print("assets generated")

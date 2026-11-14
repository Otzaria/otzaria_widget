# מדריך הגשה ל-Microsoft Store — ווידג'ט אוצריא

מסמך אחד עם כל מה שצריך למלא ב-Partner Center, לפי סדר דפי ההגשה. כל בלוק שמסומן כטקסט להדבקה מוכן להעתקה.
סימון **⚠️** = החלטה או מידע אמיתי שרק הבעלים יכול לתת. שמות כמו `<PLACEHOLDER>` חייבים להיות מוחלפים לפני ההגשה.
מגבלות התווים מצוינות ליד כל שדה (נכון לזמן הכתיבה; ה-Partner Center מציג את המגבלה בפועל ליד כל שדה, ואם יש הבדל, הוא קובע).

---

> **הערת בעלות:** המפרסם הוא הבעלים של אוצריא, ולכן אין צורך באישור צד שלישי לרישיון, להעברת הנתונים או לשם.
---

## 0. דרישות מוקדמות והערכים להעתקה מ-Product identity

### דרישות מוקדמות

- חשבון מפתח ב-Partner Center (Windows & Xbox / "Apps and games"), בתשלום חד-פעמי; סוג חשבון Individual או Company.
- ⚠️ אימות זהות מושלם בחשבון (לחשבון Company: אימות עסק; יכול לקחת ימים).
- מחשב עם .NET 10 SDK ו-Windows SDK 10.0.26100 (ראו `docs/DEVELOPMENT.md` סעיף 5).
- כתובת אינטרנט ציבורית למדיניות הפרטיות (סעיף 2 למטה).

### שלבים

1. ב-Partner Center: **Apps and games ← New product ← MSIX or PWA app**.
2. **שמרו את שם המוצר** (Reserve product name). שמרו את שני השמות, כל אחד בשפה שלו:
   - עברית (he-IL): `ווידג'ט אוצריא`
   - אנגלית (en-US): `Otzaria Widget`
   - ⚠️ אם השם תפוס, שם חלופי מוצע: `Widgets for Otzaria` / `ווידג'טים לאוצריא`.
3. אחרי שמירת השם: **Product management ← Product identity**. מעתיקים שלושה ערכים:

| שדה ב-Partner Center | פרמטר ב-`make-msix.ps1` | לאן זה נכנס בחבילה | הערך שלכם |
|---|---|---|---|
| Package/Identity/Name | `-IdentityName` | `Identity Name` במניפסט | ⚠️ `<PLACEHOLDER: למשל 12345Publisher.OtzariaWidget>` |
| Package/Identity/Publisher (`CN=<GUID>`) | `-Publisher` | `Identity Publisher` | ⚠️ `<PLACEHOLDER: CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX>` |
| Package/Properties/PublisherDisplayName | `-PublisherDisplayName` | `PublisherDisplayName` | ⚠️ `<PLACEHOLDER>` (שם המפרסם כפי שיוצג בחנות) |

4. בונים חבילת חנות (לא חתומה; החנות חותמת בעצמה ומתקינה את Windows App Runtime 1.8 כתלות). מפורט ב-`docs/DEVELOPMENT.md` סעיף 10:

```powershell
.\packaging\make-msix.ps1 -Store `
  -IdentityName '<IdentityName>' `
  -Publisher 'CN=<GUID>' `
  -PublisherDisplayName '<PublisherDisplayName>'
```

התוצאה: `artifacts\store\Otzaria.Widget_<גרסה>_x64.msix`. בלי שלושת הערכים הקובץ ייקרא `*_PLACEHOLDER-IDENTITY.msix` ו-Partner Center ידחה אותו.
הגרסה חייבת להיות גבוהה מכל גרסה שהוגשה קודם; ספרה ראשונה שאינה 0 וספרה רביעית 0.

---

## 1. Pricing and availability (מחיר וזמינות)

| שדה | המלצה | הערות |
|---|---|---|
| Markets | **כל השווקים** (ברירת מחדל) | הווידג'ט עובד בכל מקום שבו אוצריא בשימוש; אין תוכן מוגבל. ⚠️ אם יש שווקים שבהם אינכם רוצים להפיץ, בטלו אותם |
| Price | **Free** | הפרויקט חינמי. ⚠️ החלטה: המחיר בידי הבעלים |
| Free trial | None | לא רלוונטי |
| Visibility | **Public (Make this product available and discoverable)**. מומלץ להתחיל ב-**Private audience** (קישור ישיר לבודקים) או **Hide** כדי לבדוק את החיווי בחנות לפני גילוי ציבורי | ⚠️ החלטה: ציבורי מיד או הדרגתי |
| Schedule | **As soon as possible** | אפשר גם לתזמן תאריך |
| Organizational licensing | ברירת מחדל (לא לסמן הצעת Volume Purchase) | ללא משמעות לחינמי |
| Device family availability | Desktop בלבד | ראו סעיף 5 |
| Microsoft Store 'Make available to Windows 10/11' | ברירת מחדל | הווידג'טים פועלים רק ב-Windows 11, ראו סעיף 2 |

---

## 2. Properties (מאפיינים)

### קטגוריה

- **Category:** מומלץ **Books & reference** (ספרים ועיון). נימוק: הווידג'ט מושך את אוצריא (ספרייה של ספרי קודש) למסך הבית, והמשתמשים מחפשים בקטגוריה הזאת. חלופות: **Personalization** (ווידג'טים ללוח) או **Productivity** (לוח שנה, התקדמות).
- **Subcategory:** בחרו את התת-קטגוריה הקרובה ביותר מהרשימה שתוצג (למשל Reference, או Other / Not applicable אם קיימת). ⚠️ הרשימה משתנה; בחרו מה שנראה סביר.

### Privacy policy URL (חובה בפועל)

- ⚠️ כתובת ציבורית שבה אפשר לקרוא את המדיניות. מומלץ GitHub Pages של הריפו, או קובץ בריפו הציבורי (`https://github.com/<PLACEHOLDER>/<PLACEHOLDER>/blob/main/PRIVACY.md`). הכתובת חייבת להיות נגישה בלי התחברות.
- למה חובה: האפליקציה קוראת נתונים אישיים מקומיים (היסטוריית קריאה, סימניות). גם אם שום דבר לא יוצא מהמחשב, מדיניות הפרטיות מבטלת סיבת דחייה נפוצה.
- הטקסט המלא (בעברית ובאנגלית) להדבקה בדף שתאחסנו נמצא בנספח א' בסוף המסמך, ומבוסס אך ורק על מה שהקוד עושה בפועל (אין קריאות רשת).

### Website (אופציונלי)

⚠️ `<PLACEHOLDER: כתובת עמוד הפרויקט, למשל דף ה-GitHub>`. אפשר גם להשתמש באתר אוצריא, כי המפרסם הוא הבעלים.

### Support contact info (חובה)

⚠️ `<PLACEHOLDER: כתובת URL של Issues בגיטהאב או כתובת דוא"ל ייעודית לתמיכה>`. מומלץ כתובת ייעודית או דף Issues, ולא כתובת אישית.

### Display mode / Game settings

לא רלוונטי (אפליקציה, לא משחק). Display mode: ברירת מחדל.

### System requirements (דרישות מערכת)

Partner Center מציג שדות Minimum / Recommended. ערכים מוצעים:

- **Minimum OS:** Windows 11 (גרסה 21H2 ומעלה, x64). ⚠️ המניפסט כרגע מצהיר `MinVersion 10.0.19041.0` (Windows 10), אף שהווידג'ט פועל רק ב-Windows 11. מומלץ להעלות ל-`10.0.22000.0` כדי שהחנות לא תציע את האפליקציה למשתמשי Windows 10 (שינוי במניפסט, לא כאן). עד אז ציינו את הדרישה בבירור בתיאור.
- **Architecture:** x64.
- **Windows App Runtime 1.8:** מותקן אוטומטית מהחנות כתלות; אין צורך בפעולה ממשתמש.
- **Otzaria:** מומלץ שתהיה מותקנת (או שתיקיית הנתונים שלה קיימת במחשב) כדי לראות ספרים אחרונים, סימניות והתקדמות. בלי אוצריא פועלים זמני היום והלוח העברי (ברירת מחדל ירושלים).
- RAM / אחסון: ערכי ברירת מחדל מספיקים (החבילה כ-100 MB בגלל .NET עצמאי).

### Product declarations (הצהרות)

| הצהרה | מומלץ | הערה |
|---|---|---|
| App uses ... "This app depends on other apps" / requires accessories | לא לסמן "Accessories". ⚠️ אם יש הצהרה על תלות באפליקציה חיצונית, אפשר לסמן ולהסביר שמדובר באוצריא | אוצריא אינה בחנות. בכל מקרה יש לציין בתיאור |
| Customer is purchasing/installing ... "This app has been tested to meet accessibility guidelines" | ⚠️ לסמן רק אם בדקתם בפועל | אל תסמנו בלי בדיקה |
| Allow Microsoft to install updates | לסמן (מומלץ) | עדכונים אוטומטיים |
| Disable screen capture | לא לסמן | אין סיבה |
| Uses ads / in-app purchase | לא | אין פרסומות ואין רכישות |
| Collects personal info / uses network | **לא** | ראו נספח א' |
| Windows Desktop Extensions / other | אין | |

---

## 3. Age ratings (דירוג גילאים, שאלון IARC)

קטגוריית ה-IARC: **"All Other App Types"** (או Utility, Productivity, Collaboration or Other). התשובה לכל שאלת תוכן: **לא**.

| שאלה (בקירוב לשאלון) | תשובה מוצעת | נימוק |
|---|---|---|
| אלימות (קרטונית, ריאליסטית, דם, אלימות מינית) | לא | אין |
| פחד / אימה | לא | אין |
| מיניות / עירום | לא | אין |
| שפה גסה | לא | הווידג'ט מציג רק שמות ספרים וזמנים |
| חומרים מבוקרים (אלכוהול, טבק, סמים) | לא | אין |
| הומור גס | לא | אין |
| הימורים / הימורים מדומים | לא | אין |
| רכישות דיגיטליות / רכישות בתוך האפליקציה | לא | חינמי |
| אינטראקציה בין משתמשים / תוכן שנוצר על-ידי משתמשים | לא | אין רשת |
| שיתוף מיקום פיזי של המשתמש | לא | העיר נבחרת ידנית או נקראת מהעדפות אוצריא; אין שימוש ב-API מיקום |
| שיתוף מידע אישי עם צד שלישי | לא | אין העברת מידע |
| גישה לא מסוננת לאינטרנט | לא | אין דפדפן ואין קריאות רשת |
| אזכור דת | ⚠️ אם השאלון שואל, התוכן הדתי הוא ספרי יהדות (שמות ספרים בלבד) | להשיב כמידע בלבד; לא מוגדר כתוכן רגיש ברוב הגרסאות של השאלון |

תוצאה צפויה: **ללא הגבלת גיל (3+/Everyone)**. ⚠️ ענו בכנות; תשובה לא נכונה עלולה לגרום להסרה.

---

## 4a. Store listing - עברית

### Product name (עד 100 תווים)

```
ווידג'ט אוצריא
```

### Description (עד 10,000 תווים)

```
ווידג'טים ללוח הווידג'טים של Windows 11 (Win + W) עבור אוצריא, וכן אפליקציית הגדרות קטנה. לחיצה על פריט בווידג'ט פותחת אותו ישירות באוצריא, במקום המדויק.

שישה ווידג'טים, בגדלים קטן, בינוני וגדול:
• אוצריא · ספרים אחרונים: "המשך קריאה" וספרים אחרונים, עם המיקום בכל ספר (למשל דף או פרק).
• אוצריא · סימניות: הסימניות שלכם, עם הכותרת, הספר והמיקום. אפשר לסנן לפי קבוצה.
• שמור וזכור: התקדמות הלימוד בספרים, עם פסי התקדמות ואחוזים.
• זמני היום: העיר, התאריך העברי, הזמן הבא וספירה לאחור אליו.
• לוח עברי: התאריך העברי והלועזי, חגים, הדלקת נרות והבדלה, פרשת השבוע, ספירת העומר, דף יומי בבלי ודף יומי ירושלמי.
• אוצריא · לוח אישי: לוח משולב מהחלקים שתבחרו ובסדר שתקבעו.

כל ווידג'ט ניתן להוספה כמה פעמים, ולכל עותק הגדרות משלו. ההתאמה האישית נעשית מתפריט ה-... של הווידג'ט.

אפליקציית ההגדרות (בעברית או באנגלית, לפי שפת Windows; אפשר להחליף בהגדרות) מאפשרת לראות מה זוהה במחשב, לקבוע ידנית את מיקום הנתונים אם צריך, לבחור עיר, זמנים, פורמט שעה ומעבר היום העברי, ולהחליט איך נפתחים קישורים. כל שינוי נשמר אוטומטית.

פרטיות:
• קריאה בלבד: הווידג'ט קורא את הקבצים המקומיים של אוצריא ולעולם אינו כותב או משנה אותם.
• עובד גם כשאוצריא סגורה.
• שום מידע לא יוצא מהמחשב: אין שרתים, אין איסוף נתונים ואין חשבון.
• אפשר להסתיר חיפושים אחרונים מכל הווידג'טים.

דרישות:
• Windows 11 (64 סיביות), שבו קיים לוח הווידג'טים.
• Windows App Runtime 1.8.
• אוצריא מותקנת במחשב, או לפחות תיקיית הנתונים שלה. אוצריא אינה חייבת להיות פתוחה.

הווידג'ט הוא ווידג'ט נלווה לאוצריא, ולא נעשה באוצריא שום שינוי.
```

### What's new in this version (עד 1,500 תווים)

```
גרסה ראשונה: שישה ווידג'טים ללוח הווידג'טים של Windows 11 (ספרים אחרונים, סימניות, שמור וזכור, זמני היום, לוח עברי ולוח אישי), בגדלים קטן, בינוני וגדול, ואפליקציית הגדרות בעברית ובאנגלית.
```

### Product features (עד 20 פריטים, עד 200 תווים לכל פריט; מומלצים 5-8)

1. שישה ווידג'טים בגדלים קטן, בינוני וגדול: ספרים אחרונים, סימניות, שמור וזכור, זמני היום, לוח עברי ולוח אישי.
2. לחיצה על פריט פותחת אותו ישירות באוצריא, במקום המדויק בספר.
3. זמני היום עם ספירה לאחור לזמן הבא, ולוח עברי עם חגים, פרשת השבוע, ספירת העומר ודף יומי (בבלי וירושלמי).
4. התאמה אישית לכל ווידג'ט, והוספה של אותו ווידג'ט כמה פעמים עם הגדרות שונות.
5. אפליקציית הגדרות בעברית ובאנגלית (לפי שפת Windows, ואפשר להחליף), עם בחירת עיר, זמנים ופורמט שעה.
6. קריאה בלבד: לא כותב ולא משנה את קבצי אוצריא, ועובד גם כשאוצריא סגורה.
7. פרטיות: אין שרתים, אין איסוף נתונים ואין חשבון. אפשר להסתיר חיפושים אחרונים.

### Search terms (עד 7 ביטויים, עד 30 תווים לכל ביטוי)

1. אוצריא
2. ווידג'ט
3. זמני היום
4. לוח עברי
5. שמור וזכור
6. סימניות
7. דף יומי

### Copyright and trademark info (עד 200 תווים)

```
© 2026 [שם המפרסם]. כל הזכויות שמורות. "אוצריא" ושמות אחרים שייכים לבעליהם.
```

הערה: יש להחליף את [שם המפרסם] בשם המפרסם כפי שהוא רשום ב-Partner Center. בבדיקה (פחות מ-200 תווים).


## 4b. Store listing - English

### Product name

שם המוצר (עד 100 תווים). חייב להיות זהה לשם שהוזמן ב-Partner Center (Reserve product name).

```
Otzaria Widget
```

### Description

תיאור (עד 10,000 תווים; מינימום 200). הטקסט מתבסס רק על מה שכתוב ב-README.

```
Otzaria Widget adds Otzaria-powered widgets to the Windows 11 Widgets Board (Win + W). See your recent books, bookmarks, study progress, daily Zmanim and the Hebrew calendar at a glance, and tap any item to open it directly in Otzaria.

This is a companion widget for Otzaria. It does not modify Otzaria in any way.

SIX WIDGETS
- Otzaria - Recent books: continue reading the tab currently open in Otzaria, or your last reading, plus more recent books with their position (for example page or chapter). Small, medium and large sizes.
- Otzaria - Bookmarks: your bookmarks with the title you gave them, the book and the position. Choose all bookmarks, ungrouped ones or one of your Otzaria bookmark groups. Medium and large sizes.
- Shamor Vezachor: study progress per book with progress bars and percentages. Small, medium and large sizes.
- Zmanim: the next daily time with a countdown, with the city and the Hebrew date in the header. When all of today's times have passed, tomorrow's times are shown. Small, medium and large sizes.
- Hebrew calendar: Hebrew and civil date, day of the week, holidays, candle lighting and Havdalah, weekly Torah portion, Omer count, and the daily Daf Yomi (Bavli and Yerushalmi). Small, medium and large sizes.
- Otzaria - Personal dashboard: one combined widget made of the parts you choose (today's date, next time, continue reading, Shamor Vezachor, bookmarks) in the order you prefer. Medium and large sizes.

Every widget can be added more than once, and each copy has its own settings.

CUSTOMIZE
Use the widget's "..." menu and choose "Customize widget" to set the number of items, show or hide the position in a book, include PDF books, choose a bookmark group, pick a city and the set of times to show, and more. A small settings app, available in Hebrew and English (it follows your Windows language and can be switched in its settings), lets you choose the city, the Zmanim to display, the time format (24-hour or 12-hour), the point at which the Hebrew day changes, and how links are opened. Changes are saved automatically.

OPEN IN OTZARIA
Each item is a link that opens the book at the exact place in Otzaria.

PRIVATE BY DESIGN
- Read only: the widget only reads Otzaria's local files and never writes to or changes them.
- Works even when Otzaria is closed: data is read straight from the files.
- Nothing leaves your computer: no servers, no data collection and no account.
- You can hide recent searches from all widgets.

REQUIREMENTS
- Windows 11 (64-bit). The Widgets Board is available only on Windows 11.
- Windows App Runtime 1.8 (x64).
- Otzaria installed, or at least its data folder present on the computer. Otzaria does not need to be running.

The widget finds Otzaria's data automatically in most cases, including the portable version of Otzaria. If needed, you can set the locations manually in the settings app.
```

### What's new in this version

מה חדש בגרסה (עד 1,500 תווים). זו הגרסה הראשונה, ולכן הטקסט הוא הודעת השקה.

```
First release.
- Six widgets for the Windows 11 Widgets Board: Recent books, Bookmarks, Shamor Vezachor, Zmanim, Hebrew calendar and a personal Dashboard.
- Small, medium and large sizes, with per-widget customization.
- Settings app in Hebrew and English for city, Zmanim, time format and data locations.
- Opens books directly in Otzaria at the exact position.
```

### Product features

תכונות המוצר (עד 20 בולטים, עד 200 תווים לכל אחד). בחרתי 8.

```
Six widgets: recent books, bookmarks, Shamor Vezachor, Zmanim, Hebrew calendar and a personal dashboard
Small, medium and large sizes, each widget can be added several times with its own settings
Tap any item to open the book in Otzaria at the exact position
Daily Zmanim with a countdown, Hebrew date, holidays, Omer count and Daf Yomi (Bavli and Yerushalmi)
Customize each widget and use the settings app (Hebrew or English) for city, times, time format and data locations
Reads Otzaria's local files in read-only mode and works even when Otzaria is closed
No account, no servers and no data collection: nothing leaves your computer
Option to hide recent searches from all widgets
```

### Search terms

מילות חיפוש (עד 7 ביטויים, עד 30 תווים לכל אחד). כל שורה היא שדה נפרד.

```
Otzaria
Hebrew calendar
Zmanim
Jewish widgets
Daf Yomi
Hebrew date
Widgets board
```

### Copyright and trademark info

זכויות יוצרים וסימנים מסחריים (עד 200 תווים). יש להחליף את YEAR ואת OWNER בשנה ובשם בעל הזכויות האמיתי. השם "Otzaria" שייך לבעליו.

```
(c) YEAR OWNER. All rights reserved. Otzaria and other names are property of their respective owners.
```


## 5. צילומי מסך ולוגואים

### 5.1 מה שהמרכז לשותפים (Partner Center) דורש

בסעיף "Store listings" > שפה > "Store logos" ו"Screenshots". הגדלים להלן לפי הידוע; כל ערך שמסומן "לבדוק במסך" יש לאמת מול הטקסט שמופיע בשדה ההעלאה עצמו, כי המרכז מעדכן דרישות מדי פעם.

| סוג תמונה | חובה? | יחס | גודל בפיקסלים | פורמט / הערות |
|---|---|---|---|---|
| צילום מסך (Desktop) | חובה, לפחות 1 (עד 10) | 16:9 מומלץ | מינימום 1366x768, מקסימום 3840x2160 (לבדוק במסך) | PNG; עד 50MB לקובץ; מומלץ 4 ומעלה |
| לוגו חנות 1:1 (App tile icon) | חובה | 1:1 | 300x300 (לבדוק במסך האם 150x150 או 71x71 גם מתקבלים) | PNG |
| פוסטר 2:3 (Poster art) | מומלץ | 2:3 | 720x1080 (לבדוק במסך) | PNG; משפר חשיפה בחיפוש ובדפי קטגוריה |
| פוסטר 9:16 | אופציונלי | 9:16 | 720x1280 (לבדוק במסך) | PNG |
| Box art 1:1 | אופציונלי | 1:1 | 1080x1080 (לבדוק במסך) | PNG |
| Hero / Super hero art | אופציונלי | 16:9 | 1920x1080 (לבדוק במסך) | PNG |

### 5.2 התמונות הקיימות בפרויקט

תיקיית `packaging\Assets` (נכסי האריזה של האפליקציה):

| קובץ | גודל | שימוש בחנות |
|---|---|---|
| `Square150x150Logo.png` | 150x150 | אריח באריזה בלבד (Start menu); לא מתאים ללוגו החנות 1:1 (קטן מדי) |
| `Square150x150Logo.scale-200.png` | 300x300 | מתאים בגודלו ללוגו חנות 1:1 (300x300); לבדוק שהתוכן נראה טוב ושאין שוליים שקופים/ריפוד מיותר |
| `Square44x44Logo.png`, `.scale-200.png` (88x88), `targetsize-24`, `targetsize-48` | 44, 88, 24, 48 | אייקוני אפליקציה בלבד; לא לחנות |
| `StoreLogo.png` | 50x50 | לוגו פנימי של המניפסט (50x50); לא מספיק להעלאה לחנות |
| `Wide310x150Logo.png` | 310x150 | אריח רחב באריזה; יחס 2.07:1, לא מתאים לאף סלוט בחנות |
| `Wide310x150Logo.scale-200.png` | 620x300 | כנ"ל |

תיקיית `packaging\ProviderAssets` (אייקונים וצילומי תצוגה של הווידג'טים בלוח הווידג'טים):

| קבצים | גודל | שימוש בחנות |
|---|---|---|
| `Icon.png` ועוד 6 אייקונים (`Icon_Bookmarks`, `Icon_Dashboard`, `Icon_HebrewCalendar`, `Icon_RecentBooks`, `Icon_ShamorZachor`, `Icon_Zmanim`) | 64x64 כל אחד | אייקוני ספק הווידג'טים; לא לחנות |
| `Screenshot_<שם>.png` ו-`Screenshot_<שם>_dark.png` עבור Bookmarks, Dashboard, HebrewCalendar, RecentBooks, ShamorZachor, Zmanim (12 קבצים) | 300x304 כל אחד | תצוגות מקדימות של הווידג'טים; קטנות בהרבה מ-1366x768 ולכן לא תקפות כצילומי מסך בחנות |

### 5.3 סיכום: מה חסר

| סלוט בחנות | מצב | פעולה |
|---|---|---|
| צילומי מסך (לפחות 1, מומלץ 4+) | חסר | לצלם את הווידג'טים על שולחן העבודה / בלוח הווידג'טים ב-Windows 11 ברזולוציה של לפחות 1366x768 (עדיף 1920x1080), בפורמט PNG. אפשר לשלב בין מצב בהיר לכהה. אין להשתמש בקבצי 300x304 הקיימים כצילומי חנות. |
| לוגו 1:1 | קיים בקירוב | `Square150x150Logo.scale-200.png` (300x300) כמועמד; לוודא איכות, או לייצא מחדש 300x300 (עדיף גם גרסה גדולה יותר) ממקור וקטורי |
| פוסטר 2:3 (מומלץ) | חסר | ליצור 720x1080 (לבדוק במסך אם נדרש גודל אחר) |
| פוסטר 9:16, Box art, Hero | חסר, אופציונלי | ליצור לפי הצורך; לא חוסם הגשה |

הערה: אין להעלות לחנות את אייקוני 24/44/48 או `StoreLogo.png` (50x50); אלה נכסי מניפסט שנארזים בתוך ה-MSIX.


## 6. הערות לבודקים והצדקת runFullTrust

### (א) Notes for certification

השדה "Notes for certification" ב-Partner Center (שלב Properties/Submission options) מיועד לבודקי מיקרוסופט בלבד ואינו מוצג לציבור. הטקסט הבא מוכן להדבקה כפי שהוא. הוא מסביר איך בודקים ווידג'ט בלוח הווידג'טים, מה רואים כשאוצריא אינה מותקנת, ושאין צורך בחשבון או בהתחברות.

```text
Otzaria Widget is a Windows 11 Widgets Board provider (plus a small settings window) for Otzaria, a free Hebrew library app. It adds six widgets to the Widgets Board: Recent books, Bookmarks, Shamor Vezachor (study progress), Daily times (zmanim), Hebrew calendar, and a combined personal Dashboard. UI text is in Hebrew (he-IL) with en-US as a secondary resource language, and the widgets are laid out right-to-left.

NO ACCOUNT / NO SIGN-IN
The app needs no account, no sign-in, no license key and no network connection. It has no servers, collects no data and sends nothing off the device. There are no in-app purchases or ads.

REQUIREMENTS
Windows 11 x64 (the Widgets Board exists only on Windows 11). The package depends on the framework package Microsoft.WindowsAppRuntime.1.8, which the Store installs automatically. The Otzaria app is NOT required to test (see below).

HOW TO TEST
1. Install the package and wait for the installation to finish.
2. Press Win + W to open the Widgets Board.
3. Click the "+" (Add widgets) button. Pick "Otzaria Widget" in the list of providers.
   (Hebrew display name: ווידג'ט אוצריא.) Six widgets are listed with preview screenshots.
4. Click a widget to pin it to the board. Repeat for the other widgets if desired. Each widget can be added more than once and can be resized with the "..." menu (small / medium / large, depending on the widget).
5. Optional: "..." menu > "Customize widget" shows a customization card (for example, which city to use for daily times). Press "Save" to apply.
6. Optional: open "Otzaria Widget" (ווידג'ט אוצריא) from the Start menu to see the settings window. It shows what was detected on the device and offers manual city selection for the daily-times and calendar widgets. Changes are saved automatically.
If the widgets do not show up in the "Add widgets" list right after installation, the Widgets Board caches its list: sign out and back in to Windows, or restart explorer.exe, and open the board again.

WHAT YOU WILL SEE WITHOUT THE OTZARIA APP INSTALLED
Otzaria Widget reads the local data files of the Otzaria app. A test machine will normally not have Otzaria, so there is no personal data to show. This is expected behavior, not a defect:
- The Recent books, Bookmarks and Shamor Vezachor widgets show an empty-state card saying that no Otzaria data was found (in Hebrew), with an "Open settings" button. The button starts the settings window of this package.
- The Daily times and Hebrew calendar widgets are computed locally from the date and the selected city (a city can be chosen in the settings window), so they can be evaluated without Otzaria.
- The Dashboard widget combines the sections above. Sections that need Otzaria data show the same empty state.
- Tapping an item inside a widget opens it in Otzaria via an otzaria:// link. Without Otzaria installed there is nothing to open, and no item appears that could be tapped. Nothing crashes.

FULL-TRUST DESKTOP COMPONENT
The package declares the restricted capability runFullTrust. A justification is provided in the separate "Restricted capabilities" field. In short: the widget provider is an out-of-process COM server (OtzariaWidget.exe started by the Widgets host), and it must read the data files of another desktop app outside the package sandbox and start that app on request. The package makes no changes to the system, installs no services, no drivers and no startup entries, and does not modify any Otzaria files (access is strictly read-only).

SUPPORT
Everything is local. Logs are written to a provider.log file under %LOCALAPPDATA%\OtzariaWidget\ (or the package LocalCache folder) and contain no personal content. The settings window has an "About" page with a button that opens the log folder.
```

הערות לעורך (לא להדביק):

- הטענות על התנהגות בלי אוצריא נלקחו מה-README (כרטיס "לא נמצאו נתוני אוצריא — פתח את ההגדרות" עם כפתור "פתח הגדרות"). הטענה שזמני היום ולוח עברי פועלים בלי אוצריא נגזרת מכך שהחישוב נעשה מקומית (Kosher.Zmanim) ושאפשר לבחור עיר ידנית. כדאי לאמת אותה בהרצה על מחשב נקי לפני ההגשה, ולהשמיט את השורה אם התוצאה שונה.
- המשפט "no item appears that could be tapped" הוא הסקה ולא נאמר ב-README. כדאי לאמת או למחוק אותו.
- אם ההגשה תכלול מנוי/ייבוא של התלות Windows App Runtime בצורה אחרת, יש להתאים את סעיף REQUIREMENTS.

### (ב) Justification for runFullTrust

ה-capability `runFullTrust` היא מוגבלת (restricted) ומחייבת הצדקה בשדה הייעודי ב-Partner Center. הצדקה טובה צריכה לומר מה בדיוק דורש הרשאה מלאה, למה אין חלופה ב-sandbox של UWP, ומה החבילה אינה עושה. הטקסט הבא מוכן להדבקה. הוא מבוסס על ה-manifest (`windows.comServer` עם `Windows.FullTrustApplication`) ועל תיאור ההתנהגות ב-README.

```text
Package capability: runFullTrust (rescap).

WHY THE PACKAGE NEEDS IT
Otzaria Widget is a Windows 11 Widgets Board provider. Windows requires a widget provider to run as an out-of-process COM server: the Widgets host calls CoCreateInstance on the provider CLSID declared in the manifest (windows.comServer / ExeServer, OtzariaWidget.exe, started with -RegisterProcessAsComServer) and talks to it through the IWidgetProvider interface. The same executable also provides the small settings window of the app (WPF, .NET). Both are classic Win32 desktop processes (EntryPoint="Windows.FullTrustApplication"), which is why runFullTrust is required. The provider is not a UWP/AppContainer app and cannot be turned into one.

WHAT THE FULL-TRUST PROCESS DOES, AND ONLY THIS
1. Reads user files outside the package sandbox, read-only. The purpose of the widgets is to show data from the separate desktop app Otzaria (a Hebrew library application): recently opened books, bookmarks and study progress. That data lives in files that Otzaria itself created in the user's profile (for example the %APPDATA%\otzaria folder, its SQLite databases user_state.db and seforim.db, and its app_preferences.hive preferences file; portable installs keep the same files next to otzaria.exe). An AppContainer app cannot open these locations, and there is no API through which Otzaria exposes this data to other apps. The files are opened in read-only mode (also while Otzaria is running), and the package never writes to, changes or deletes any Otzaria file. From the preferences file only a small whitelist of keys is read (city, daily-times and clock settings, colors, library paths, study progress). Keys for accounts, e-mail or passwords are not read or logged.
2. Starts the Otzaria app on the user's request. When the user taps an item in a widget (for example a recently read book), the widget opens it in Otzaria with an otzaria://open/... link. By default the Widgets Board opens the link itself. As an optional, user-selectable mode (needed for portable installs of Otzaria, which are not registered for the otzaria:// protocol) the provider launches otzaria.exe with that link. Launching another desktop executable from the provider requires full trust. The same applies when a widget button starts the settings window of this package.
3. Keeps local settings and logs for itself (settings.json, provider.log) in its own package-local folder.

WHAT IT DOES NOT DO
- No network access of any kind, no account, no telemetry, no data collection. Nothing leaves the device.
- No writes to Otzaria files or to any user documents, no changes to system settings or the registry outside of the package manifest registrations.
- No Windows service, no driver, no scheduled task, no startup entry, no elevation (the package runs as the normal user, never as administrator).
- No file or process access beyond what is described above, and only in response to the Widgets host or to explicit user actions.

WHY A LESS PRIVILEGED OPTION IS NOT ENOUGH
Broad file-system access (broadFileSystemAccess) would still not allow the out-of-process COM server, and a UWP/AppContainer package cannot be a Widgets Board provider that reads another app's profile data and launches another desktop executable. Windows App SDK widget providers are Win32 packaged apps by design, so full trust is the supported and minimal model for this scenario.
```

הערות לעורך (לא להדביק):

- ההצהרות "אין גישה לרשת", "אין שירות/דרייבר/משימה מתוזמנת/רשומת הפעלה" ו"אין הרמת הרשאות" אינן כתובות מפורשות ב-README או ב-manifest במילים האלה. ה-manifest אכן אינו מצהיר על capability של רשת, על שירות או על startup task, וה-README קובע "אין שרתים, אין איסוף נתונים". לפני ההגשה כדאי לוודא בקוד שאין קריאות רשת ושאין רישום startup.
- הפסקה "WHY A LESS PRIVILEGED OPTION IS NOT ENOUGH" היא טיעון טכני כללי ולא משהו שנאמר בקבצים שנקראו. אפשר לקצר או להשמיט אם רוצים להישאר רק עם עובדות מהפרויקט.
- מיקום הנתיבים הוא מה-README (`%APPDATA%\otzaria`, `user_state.db`, `seforim.db`, `app_preferences.hive`, גרסה ניידת ליד `otzaria.exe`).
- הגבלת תווים: שדה ההצדקה ב-Partner Center מוגבל באורכו, ולכן ייתכן שיהיה צורך לקצר את הנוסח לפני ההדבקה.


## 7. צ'קליסט לפני הגשה

עוברים על הרשימה לפי הסדר. אל תלחצו על Submit לפני שכל התיבות מסומנות.

### א. הכנות

- [ ] **השם שמור.** שם האפליקציה הוזמן (Reserve) ב-Partner Center, והוא עדיין בתוקף. (ראה סעיף 0)
- [ ] **ערכי Identity הועתקו.** הערכים Package/Identity/Name, Package/Identity/Publisher ו-Publisher display name הועתקו מ-Partner Center (Product identity) במדויק, ללא רווחים מיותרים. (ראה סעיף 0)

### ב. בנייה ובדיקה

- [ ] **חבילת ה-Store נבנתה עם הזהות האמיתית.** הורצה הפקודה:
  `.\packaging\make-msix.ps1 -Store -IdentityName '<Name>' -Publisher 'CN=<GUID>' -PublisherDisplayName '<Name>'`
  והקובץ נוצר בתיקייה `artifacts\store\`. לעולם לא מעלים חבילה שנבנתה עם זהות Placeholder. (ראה סעיף 0)
- [ ] **הגרסה גבוהה מהקודמת.** מספר הגרסה גבוה מכל גרסה שהוגשה קודם, והמקטע הרביעי הוא 0 (דרישת ה-Store). בהגשה ראשונה - לוודא שהמקטע הרביעי הוא 0.
- [ ] **ה-DPI תוקן.** האפליקציה מוגדרת PerMonitorV2 (התיקון לאזהרת DPIAwarenessValidation הושלם) והחבילה נבנתה מחדש אחריו.
- [ ] **WACK רץ שוב על הקובץ הסופי.** Windows App Certification Kit הורץ על קובץ ה-MSIX הסופי מתוך `artifacts\store\` (לא על חבילה מקומית ישנה). התוצאה הצפויה: PASS, או לכל היותר אזהרה על "Blocked executables" - בדיקה אופציונלית שהממצאים בה (ספריות .NET/WPF והפעלת תהליכים על ידי האפליקציה) אינם רלוונטיים להסמכה. בריצה המקומית הקודמת: תוצאה כללית WARNING, 22 מתוך 24 בדיקות עברו, ואחת האזהרות הייתה DPIAwarenessValidation. (ראה סעיף 6)

### ג. Partner Center

- [ ] **מדיניות פרטיות באינטרנט.** כתובת ה-URL של הצהרת הפרטיות פעילה, נפתחת ללא התחברות, והוזנה בשדה המתאים. (ראה סעיף 2)
- [ ] **דירוג גיל.** שאלון ה-Age ratings מולא ודירוג הגיל התקבל. (ראה סעיף 3)
- [ ] **מחירים וזמינות.** מחיר (או חינם), שווקים וחשיפה (Visibility) הוגדרו. (ראה סעיף 1)
- [ ] **Properties.** קטגוריה, תת-קטגוריה, מדיניות פרטיות ושאר שדות ה-Properties מולאו. (ראה סעיף 2)
- [ ] **Listings בעברית ובאנגלית.** שני ה-Store listings (תיאור, תכונות, מילות מפתח, מידע נוסף) מולאו ונבדקו לשגיאות הקלדה. (ראה סעיפים 4א ו-4ב)
- [ ] **צילומי מסך ולוגואים.** הועלו צילומי מסך בגודל הנדרש ולוגואים תקינים לכל שפה. (ראה סעיף 5)
- [ ] **Notes for certification.** ההערות לבודקי ההסמכה הוזנו, כולל הסבר איך להפעיל את הווידג'ט ולבדוק אותו. (ראה סעיף 6)
- [ ] **הצדקה ל-runFullTrust.** הוזנה ההסבר לשימוש ביכולת runFullTrust (אפליקציית Win32/.NET שנארזת ב-MSIX, ולכן היכולת הכרחית), גם כתשובה לשאלת הבודקים על "Blocked executables". (ראה סעיף 6)
- [ ] **חבילה הועלתה.** קובץ ה-MSIX הסופי הועלה בשלב Packages, ו-Partner Center אישר אותו ללא שגיאות ואזהרות על זהות או גרסה.

### ד. שליחה

- [ ] **Submit to the Store.** כל הסעיפים ב-Partner Center מסומנים כהושלמו, ולחצתי על Submit.

### מה קורה אחרי ההגשה

ההגשה עוברת בדיקות אוטומטיות ואחריהן הסמכה (Certification). ההסמכה נמשכת בדרך כלל עד כמה ימי עסקים; במקרים מסוימים היא ארוכה יותר, ולכן כדאי לא לתכנן תאריך פרסום קשיח. את הסטטוס אפשר לעקוב אחריו ב-Partner Center, ובסיום תגיע הודעה בדוא"ל. אם ההגשה נדחית, נכנסים לדוח ההסמכה (Certification report), קוראים איזו בדיקה נכשלה ומה הסיבה, מתקנים את הבעיה (בחבילה, ברישום או בהערות לבודקים), ומגישים מחדש. אם צריך לשנות את החבילה עצמה - מעלים אותה עם גרסה גבוהה יותר. אם הדחייה נראית שגויה, אפשר להשיב לבודקים דרך Partner Center ולהסביר, למשל שהאפליקציה זקוקה ל-runFullTrust (ראה סעיף 6). עדכונים עתידיים מוגשים כהגשה חדשה (Update) באותו מוצר: כל חבילה חדשה חייבת לשאת מספר גרסה גבוה מהקודמת, כשהמקטע הרביעי נשאר 0, והחבילה נבנית שוב באותו פקודת `-Store` ועם אותם ערכי Identity. לפני כל עדכון מריצים WACK מחדש על הקובץ הסופי.



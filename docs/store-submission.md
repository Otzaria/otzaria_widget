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

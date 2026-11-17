# ווידג'ט אוצריא ל-macOS ול-iOS

אותם ווידג'טים כמו ב-Windows (ספרים אחרונים, סימניות, שמור וזכור, זמני היום, לוח עברי, לוח אישי), מבוססים על WidgetKit ו-SwiftUI.
המבנה:

- `OtzariaKit/` - חבילת Swift: קריאת נתוני אוצריא (`OtzariaCore`), לוח וזמנים (`OtzariaCalendar`), וממשק הווידג'טים (`OtzariaWidgetsUI`).
- `App/OtzariaWidget.xcodeproj` - ארבעה יעדים: אפליקציית macOS (`OtzariaWidgetMac`) והרחבת הווידג'טים שלה, אפליקציית iOS (`OtzariaWidgetiOS`) והרחבת הווידג'טים שלה.

## דרישות

- Xcode 16 ומעלה, macOS 14 ומעלה (ליעד macOS), iOS 17 ומעלה (ליעד iOS).
- אוצריא מותקנת ב-Mac (הנתונים ב-`~/Library/Application Support/otzaria`). ב-iOS אין גישה לנתוני אוצריא, ראו [מגבלה ב-iOS](#מגבלה-ב-ios).

## בנייה

```bash
xcodebuild -project apple/App/OtzariaWidget.xcodeproj -scheme OtzariaWidgetMac \
  -destination 'platform=macOS' CODE_SIGNING_ALLOWED=NO build

xcodebuild -project apple/App/OtzariaWidget.xcodeproj -scheme OtzariaWidgetiOS \
  -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build
```

בלי חתימה אפשר לבנות ולהריץ מקומית, אבל הווידג'טים לא יראו את הנתונים (אין App Group). לשימוש אמיתי חותמים.

## חתימה והגדרת Team

1. ב-Xcode בוחרים בכל אחד מארבעת היעדים את אותו Team (Signing & Capabilities).
2. מזהי ה-App Group:
   - macOS: `$(TeamIdentifierPrefix)org.otzaria.widget` (למשל `ABCDE12345.org.otzaria.widget`), באפליקציה ובהרחבה.
   - iOS: `group.org.otzaria.widget`, באפליקציה ובהרחבה.
3. מזהי החבילות: `org.otzaria.widget` (האפליקציה) ו-`org.otzaria.widget.widgets` (ההרחבה).
4. אפליקציית ה-Mac אינה ב-Sandbox, כדי לקרוא את תיקיית הנתונים של אוצריא (הפצה מחוץ ל-App Store, עם Developer ID). ההרחבה כן ב-Sandbox.

המזהה של ה-App Group נקרא בזמן ריצה מהמפתח `OtzariaAppGroup` ב-Info.plist של כל יעד (`Config/`).

## איך זה עובד ב-Mac

הרחבת ווידג'טים רצה ב-Sandbox ולכן אינה יכולה לקרוא את נתוני אוצריא. אפליקציית ה-Mac עושה את זה במקומה:

1. האפליקציה רצה בשורת התפריטים (אפשר להפעיל אותה אוטומטית בכניסה למחשב: **תצוגה והתנהגות** ← **הפעל את שירות הווידג'טים בכניסה למחשב**).
2. כל כ-45 שניות, וגם כשהאפליקציה חוזרת להיות פעילה, היא בודקת (`refreshIfChanged`, בדיקה זולה לפי חתימות הקבצים) אם הקבצים של אוצריא השתנו.
3. אם התוכן השתנה, היא כותבת `widget-snapshot.json` ו-`widget-settings.json` לתיקיית ה-App Group וקוראת ל-`WidgetCenter.reloadAllTimelines()`. בלי שינוי אין כתיבה ואין רענון.
4. הרחבת הווידג'טים קוראת את הקובץ. הלוח והזמנים מחושבים בתוך ההרחבה.

הקריאה מאוצריא היא לקריאה בלבד, ושום דבר אינו נכתב לקבצי אוצריא. ההגדרות (עיר, זמנים, שעון, מעבר יום, הסתרת חיפושים) נשמרות בקובץ ההגדרות של הווידג'ט בלבד.
הקישור `otzaria-widget://settings` פותח את חלון ההגדרות (אפשר גם `otzaria-widget://settings/calendar`).

למפתחים: בבניית Debug המשתנה `OTZARIA_WIDGET_GROUP_DIR` מפנה את תיקיית ה-App Group לתיקייה רגילה (לבדיקה בלי חתימה).

## מגבלה ב-iOS

אפליקציה אחרת אינה יכולה לקרוא את הנתונים של אוצריא ב-iOS. לכן:

- זמני היום והלוח העברי עובדים מיד, לפי ההגדרות שנשמרות באפליקציה.
- ווידג'טים שמציגים נתוני אוצריא יציגו הסבר, עד שאוצריא עצמה תכתוב את `widget-snapshot.json` ל-App Group `group.org.otzaria.widget` (שינוי עתידי בצד אוצריא). פורמט הקובץ מוגדר ב-`OtzariaKit/Sources/OtzariaCore/Widget/WidgetSnapshot.swift`.

## רישיון ו-LGPL

חישובי הלוח והזמנים מבוססים על KosherSwift (LGPL-2.1). הספרייה נכללת רק בתוך `OtzariaWidgetsUI.framework` דינמי, בעותק אחד, שמוטמע באפליקציה. אפליקציות ההרחבות אינן מכילות אותה, וההרחבות טוענות את ה-framework מתיקיית ה-Frameworks של האפליקציה.
זה דורש ב-`OtzariaKit/Package.swift`: המוצר `OtzariaWidgetsUI` מסוג `.dynamic`, והמוצר `OtzariaCalendar` בלי `type: .dynamic`. אין לשנות את הקישור לסטטי בלי לבדוק מחדש את הציות לרישיון.

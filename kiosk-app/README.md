# KioskApp - אפליקציית עמדת הקיוסק לספרייה

אפליקציית Windows (WPF, .NET 8) שנועלת מחשב ציבורי ומאפשרת גישה רק לתוכנות
מאושרות, תוך ניהול משתמשים, יתרות והדפסה בתשלום - ראו את מסמך האפיון המלא
ששימש בסיס לפרויקט הזה.

## ⚠️ הערה חשובה על מצב הקוד

הקוד כאן **נכתב ידנית, בלי גישה למהדר C#/.NET** (הסביבה שבה נכתב הקוד היא
Linux, ו-WPF/.NET Desktop דורשים Windows כדי להתקמפל בכלל - וגם ניסיון להתקין
את ה-SDK עצמו נחסם ע"י מדיניות הרשת שם). שכבת הלוגיקה הטהורה
(`src/KioskApp.Core`) לפחות נבדקה מבחינה מבנית (איזון סוגריים, XML תקין),
ויש לה סוויטת בדיקות (`src/KioskApp.Core.Tests`) שכתובה ומוכנה - אבל
**אף שורת קוד כאן לא רצה בפועל דרך מהדר אמיתי**. הצעד הראשון בכל עבודה על
הקוד הזה הוא `dotnet build` על מחשב Windows אמיתי ותיקון כל מה שהמהדר ימצא.

## מבנה הפרויקט

```
kiosk-app/
├── src/
│   ├── KioskApp.Core/         ספריית לוגיקה טהורה (net8.0, לא תלוי Windows):
│   │                          לקוח ה-API מול השרת (KioskApiClient), מודלים,
│   │                          מצב ה-session. אין כאן שום קוד UI.
│   ├── KioskApp.Core.Tests/   בדיקות xUnit ל-KioskApp.Core
│   └── KioskApp/              אפליקציית ה-WPF עצמה (net8.0-windows):
│       ├── Views/             LoginWindow, AppPickerWindow, OverlayWindow, TopupWindow
│       ├── Services/          PrintMonitorService (WMI), ProcessLauncher, AppConfig
│       └── appsettings.json   הגדרות (כתובת האתר, מפתח העמדה) - לערוך לפני הרצה!
├── setup/                     סקריפטי PowerShell להגדרת המחשב כעמדת קיוסק
├── installer/                 סקריפט Inno Setup ליצירת מתקין
└── DEPLOYMENT.md              המדריך המלא: מהתקנת הסביבה ועד עמדה חיה בספרייה
```

## דרישות מוקדמות (על מחשב הפיתוח, Windows)

1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. **Windows Desktop workload** מותקן (חלק מ-.NET SDK, אבל ודאו ש-`dotnet --list-runtimes`
   מראה גם `Microsoft.WindowsDesktop.App`, לא רק `Microsoft.NETCore.App`)
3. [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) - כמעט
   תמיד כבר מותקן כברירת מחדל ב-Windows 10/11 עדכני; אם לא, יש להתקין לפני הרצת
   האפליקציה (לא רק לפני הבנייה)
4. Visual Studio 2022 (מומלץ, יש עורך XAML חזותי) או VS Code + C# Dev Kit

## בנייה

```powershell
cd kiosk-app/src
dotnet restore
dotnet build
```

אם יש שגיאות קומפילציה - זה צפוי (ראו האזהרה למעלה). לרוב אלו יהיו טעויות
קטנות (using חסר, שם מתודה לא מדויק) שקל לתקן.

## הרצת הבדיקות (החלק היחיד שבאמת "נבדק" בסביבת הפיתוח המקורית - מבחינה לוגית בלבד)

```powershell
cd kiosk-app/src
dotnet test KioskApp.Core.Tests
```

## הרצה מקומית (לפני התקנה על העמדה בפועל)

1. ערכו את `src/KioskApp/appsettings.json`:
   ```json
   {
     "ApiBaseUrl": "https://YOUR-SITE-NAME.netlify.app",
     "StationApiKey": "<אותו ערך כמו LIBRARY_STATION_API_KEY בנטליפיי>",
     "StationId": "library-station-1"
   }
   ```
2. `dotnet run --project src/KioskApp`
3. ⚠️ באפליקציה כפי שהיא, `LoginWindow` נטענת עם `WindowState="Maximized"` +
   `Topmost="True"` + בלי כפתור סגירה - **לצורך פיתוח/בדיקה מקומית מומלץ להוסיף
   זמנית מקש יציאה** (למשל `Alt+F4` כבר עובד כברירת מחדל ב-WPF, אבל אם תרצו
   לבדוק את מסך בחירת התוכנה בלי לעבור דרך login אמיתי, הוסיפו זמנית קיצור
   דרך ל-DEBUG). אל תשאירו "דלת אחורית" כזו בגרסה שתותקן בפועל על העמדה.

## פרסום (Build ל-exe מוכן להפצה)

```powershell
dotnet publish src/KioskApp -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

הפלט ייכנס ל-`src/KioskApp/bin/Release/net8.0-windows/win-x64/publish/`.
המשיכו ל-`DEPLOYMENT.md` להתקנה בפועל על מחשב הספרייה.

## מגבלות ידועות (חשוב לקרוא לפני שסומכים על המערכת בפועל)

1. **חסימת הדפסה היא best-effort, לא ערבות מוחלטת.** האירוע ש-Windows שולח
   כשעבודת הדפסה נוצרת (WMI, `Win32_PrintJob`) נורה אחרי שהעבודה כבר בתור -
   הביטול קורה תוך שבריר שנייה, אבל במדפסות מהירות/עבודות עמוד בודד יתכן
   שהעמוד כבר יודפס לפני שהביטול מספיק. ראו `Services/PrintMonitorService.cs`.
2. **תיבות דו-שיח של קבצים (Open/Save) בתוכנה המורשית הן וקטור בריחה קלאסי
   מקיוסקים** - אם "אוצר החכמה" (או תוכנה אחרת שתאושר) חושפת תיבת "שמירה בשם"
   או "פתיחה", ייתכן שאפשר לנווט דרכה למערכת הקבצים ואף להריץ תוכנות אחרות
   (למשל ע"י הקלדת `cmd.exe` בשורת הכתובת). זו מגבלה מובנית של השיטה (Shell
   מותאם אישית), לא באג בקוד הזה. אם זה חשש ממשי - שווה לבדוק App Locker /
   Software Restriction Policies כשכבת הגנה נוספת.
3. **מיקום חלון התוכנה החיצונית מתחת ל-Overlay הוא best-effort** - תלוי
   שהתוכנה יוצרת חלון "רגיל" (לא true-fullscreen משל עצמה). בדקו בפועל מול
   "אוצר החכמה".

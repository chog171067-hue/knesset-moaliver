; סקריפט Inno Setup ליצירת מתקין (installer.exe) עבור KioskApp.
; לא ניתן להריץ Inno Setup בסביבת הפיתוח הזו (דורש Windows) - יש להוריד את
; Inno Setup (חינמי, https://jrsoftware.org/isinfo.php) על מחשב Windows,
; לפתוח את הקובץ הזה ולבחור Build > Compile.
;
; לפני הקומפילציה: להריץ קודם `dotnet publish` (ראו kiosk-app/README.md) ולוודא
; שהתיקייה SourceDir למטה מצביעה על תיקיית הפלט הנכונה.

#define MyAppName "KioskApp"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "בית הכנסת מוהליבר"
#define SourceDir "..\src\KioskApp\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{B6E1E9C4-6C3B-4B7E-9B7E-KIOSKAPP0001}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName=C:\KioskApp
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir=Output
OutputBaseFilename=KioskApp-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; appsettings.json נשמר בנפרד מהאזור שנדרס בעדכון גרסה - כדי לא לאבד את ההגדרות
; המקומיות (כתובת האתר, מפתח העמדה) בכל התקנה מחדש. ראו הערה ב-[Code] למטה.

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\KioskApp.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\KioskApp.exe"

[Code]
// אם כבר קיים appsettings.json מותקן קודם (עם הגדרות אמיתיות) - לא לדרוס אותו
// בהתקנה/עדכון חוזר. ה-installer מעתיק את appsettings.json.sample בלבד למי שמתקין
// לראשונה (ראו [Files] - יש לשנות ל-appsettings.json.sample ולהוסיף העתקה תנאית
// כאן אם רוצים למנוע דריסה אוטומטית של הגדרות קיימות בעדכון גרסה).

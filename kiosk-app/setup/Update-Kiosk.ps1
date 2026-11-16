<#
.SYNOPSIS
    עדכון KioskApp בפקודה אחת, ישירות במחשב שעליו הוא מותקן.

.DESCRIPTION
    מוריד את הגרסה העדכנית של הקוד מ-GitHub, בונה אותה, סוגר את הקיוסק אם הוא פתוח,
    ומעתיק את הקבצים החדשים לתיקיית ההתקנה. קובץ ההגדרות (appsettings.json) בתיקיית
    ההתקנה לא נדרס - הוא נשאר כמו שהוא.

    דרישות (פעם אחת במחשב): Git ו-.NET 8 SDK מותקנים, ועותק של הריפו (git clone).
    יש להריץ מ-PowerShell "כמנהל" (כדי שתהיה הרשאת כתיבה לתיקיית ההתקנה):

        powershell -ExecutionPolicy Bypass -File <תיקיית הריפו>\kiosk-app\setup\Update-Kiosk.ps1

.PARAMETER Branch
    הענף ב-GitHub שממנו בונים. ברירת מחדל: main.

.PARAMETER InstallDir
    תיקיית ההתקנה של הקיוסק. ברירת מחדל: C:\KioskApp
#>
param(
    [string]$Branch = "main",
    [string]$InstallDir = "C:\KioskApp"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$publishDir = Join-Path $env:TEMP "kiosk-publish"

Write-Host "=== 1/4 מוריד את הקוד העדכני (ענף $Branch) ===" -ForegroundColor Cyan
git -C $repoRoot fetch origin
if ($LASTEXITCODE -ne 0) { throw "הורדת הקוד מ-GitHub נכשלה (בדקו חיבור לאינטרנט)." }
git -C $repoRoot checkout -B $Branch "origin/$Branch"
if ($LASTEXITCODE -ne 0) { throw "מעבר לענף $Branch נכשל." }

Write-Host "=== 2/4 בונה את הקיוסק ===" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $repoRoot "kiosk-app\src\KioskApp") -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "הבנייה נכשלה - ראו את השורות האדומות למעלה. הקיוסק המותקן לא השתנה." }

Write-Host "=== 3/4 סוגר את הקיוסק אם הוא פתוח ===" -ForegroundColor Cyan
Get-Process -Name KioskApp -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "=== 4/4 מעתיק ל-$InstallDir (בלי לגעת ב-appsettings.json) ===" -ForegroundColor Cyan
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir | Out-Null }
robocopy $publishDir $InstallDir /E /XF appsettings.json /NFL /NDL /NJH /NJS /NP | Out-Null
# robocopy: קוד יציאה 0-7 = הצלחה, 8 ומעלה = כשל
if ($LASTEXITCODE -ge 8) { throw "ההעתקה ל-$InstallDir נכשלה (קוד $LASTEXITCODE). האם PowerShell הופעל כמנהל?" }

if (-not (Test-Path (Join-Path $InstallDir "appsettings.json"))) {
    Write-Host "שימו לב: אין appsettings.json ב-$InstallDir - יש ליצור אותו לפני הפעלת הקיוסק." -ForegroundColor Yellow
}

$commit = git -C $repoRoot log -1 --format="%h %s"
Write-Host ""
Write-Host "=== העדכון הושלם ===" -ForegroundColor Green
Write-Host "גרסה: $commit"
Write-Host "להפעלה: $InstallDir\KioskApp.exe"

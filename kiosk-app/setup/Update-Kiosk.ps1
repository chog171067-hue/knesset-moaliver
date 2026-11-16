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
# ההודעות למסך באנגלית: חלון PowerShell 5.1 הרגיל לא מציג עברית כראוי
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$publishDir = Join-Path $env:TEMP "kiosk-publish"

Write-Host "=== 1/4 Downloading latest code (branch: $Branch) ===" -ForegroundColor Cyan
git -C $repoRoot fetch origin
if ($LASTEXITCODE -ne 0) { throw "Download from GitHub failed (check the internet connection)." }
git -C $repoRoot checkout -B $Branch "origin/$Branch"
if ($LASTEXITCODE -ne 0) { throw "Switching to branch $Branch failed." }

Write-Host "=== 2/4 Building the kiosk ===" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $repoRoot "kiosk-app\src\KioskApp") -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "BUILD FAILED - see the red lines above. The installed kiosk was NOT changed." }

Write-Host "=== 3/4 Closing the kiosk if it is running ===" -ForegroundColor Cyan
Get-Process -Name KioskApp -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "=== 4/4 Copying to $InstallDir (appsettings.json is kept as is) ===" -ForegroundColor Cyan
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir | Out-Null }
robocopy $publishDir $InstallDir /E /XF appsettings.json /NFL /NDL /NJH /NJS /NP | Out-Null
# robocopy: קוד יציאה 0-7 = הצלחה, 8 ומעלה = כשל
if ($LASTEXITCODE -ge 8) { throw "Copy to $InstallDir failed (code $LASTEXITCODE). Was PowerShell started as Administrator?" }

if (-not (Test-Path (Join-Path $InstallDir "appsettings.json"))) {
    Write-Host "WARNING: no appsettings.json in $InstallDir - create it before starting the kiosk." -ForegroundColor Yellow
}

$commit = git -C $repoRoot log -1 --format="%h %s"
Write-Host ""
Write-Host "=== UPDATE COMPLETED SUCCESSFULLY ===" -ForegroundColor Green
Write-Host "Version: $commit"
Write-Host "To start: $InstallDir\KioskApp.exe"

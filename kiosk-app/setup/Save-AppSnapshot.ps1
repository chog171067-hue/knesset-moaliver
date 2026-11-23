<#
.SYNOPSIS
    שמירת "תמונה נקייה" של תיקיית מצב של תוכנה - הקיוסק מחזיר אליה את התיקייה בכל ניתוק.

.DESCRIPTION
    -Folder : התיקייה כפי שחשבון הקיוסק רואה אותה (אותה תיקייה שנכתבה בדף הניהול ב"תיקיות לאיפוס
              בניתוק", אבל בנתיב מלא). למשל: C:\Users\kiosk-user\AppData\Roaming\otzaria
    -Source : (לא חובה) מאיפה לקחת את התוכן הנקי. ברירת מחדל: התיקייה עצמה.
              למשל ההגדרות של user1: C:\Users\user1\AppData\Roaming\otzaria

    לפני ההרצה: לסגור את התוכנה, כשהיא במצב הרצוי (מסך הבית, ההגדרות והתוספים כמו שצריך).
    מריצים מ-PowerShell "כמנהל":

        powershell -ExecutionPolicy Bypass -File C:\KioskSource\kiosk-app\setup\Save-AppSnapshot.ps1 -Folder "C:\Users\kiosk-user\AppData\Roaming\otzaria" -Source "C:\Users\user1\AppData\Roaming\otzaria"
#>
param(
    [Parameter(Mandatory = $true)][string]$Folder,
    [string]$Source = ""
)

$ErrorActionPreference = "Stop"
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "PowerShell is NOT running as administrator. Start -> PowerShell -> 'Run as administrator'." -ForegroundColor Red
    exit 1
}

$Folder = $Folder.Trim().Trim('"').TrimEnd('\')
if ($Source -eq "") { $Source = $Folder }
$Source = $Source.Trim().Trim('"').TrimEnd('\')
if (-not (Test-Path $Source)) { Write-Host "Source folder not found: $Source" -ForegroundColor Red; exit 1 }

$root = "C:\KioskSnapshots"
if (-not (Test-Path $root)) {
    New-Item -ItemType Directory -Path $root | Out-Null
}
# מערכת ומנהלים - מלא; משתמשים רגילים (כולל חשבון הקיוסק) - קריאה בלבד
icacls $root /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-32-545:(OI)(CI)RX" | Out-Null

# אותו חישוב כמו בקיוסק (AppStateReset.SnapshotKey)
$key = (($Folder -replace '[:\\/]+', '_')).ToLowerInvariant()
$dst = Join-Path $root $key

if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
robocopy $Source $dst /E /XD logs /XF *.lock /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Write-Host "Copy failed (code $LASTEXITCODE)" -ForegroundColor Red; exit 1 }

$count = (Get-ChildItem $dst -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
Write-Host ""
Write-Host "=== CLEAN SNAPSHOT SAVED ===" -ForegroundColor Green
Write-Host "Folder:   $Folder"
Write-Host "From:     $Source"
Write-Host "Saved in: $dst ($count files)"
Write-Host "The kiosk will reset this folder to the snapshot on every logout (and at kiosk start)."

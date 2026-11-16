#Requires -RunAsAdministrator
<#
.SYNOPSIS
    נועל את המחשב כעמדת קיוסק - רק עבור משתמש ייעודי (kiosk-user). חשבון המנהל
    וכל שאר המשתמשים נשארים עם Windows רגיל.

.DESCRIPTION
    מה הסקריפט עושה:
    1. יוצר משתמש ייעודי בהרשאות רגילות (לא מנהל), אם הוא לא קיים.
    2. יוצר לו פרופיל (תיקיית משתמש) אם עוד אין.
    3. בהגדרות של המשתמש הזה בלבד: KioskApp במקום שולחן העבודה (Shell), חסימת
       מנהל המשימות, נעילת המסך, שינוי סיסמה, חלון "הפעלה" והפעלה אוטומטית מ-USB.
    4. התחברות אוטומטית למשתמש הקיוסק בכל הפעלה של המחשב.

    חשוב: ה-Shell נקבע רק למשתמש הקיוסק (HKCU שלו), לא לכל המחשב. גרסה קודמת של
    הסקריפט קבעה אותו לכל המחשב, וכך גם המנהל קיבל מסך שחור.

    כניסה כמנהל לתחזוקה:
      Ctrl+Alt+Delete ← "יציאה", ולהחזיק את מקש Shift לחוץ עד שמופיע מסך הכניסה
      (Shift מונע את ההתחברות האוטומטית). אז בוחרים את חשבון המנהל ונכנסים כרגיל.

    ביטול כל הנעילה: Remove-KioskLockdown.ps1 שבתיקייה הזו.

    הרצה (PowerShell כמנהל):
      powershell -ExecutionPolicy Bypass -File C:\KioskSource\kiosk-app\setup\Setup-KioskUser.ps1

.PARAMETER KioskUsername
    שם המשתמש הייעודי לעמדה. ברירת מחדל: kiosk-user

.PARAMETER KioskAppPath
    הנתיב המלא ל-KioskApp.exe. ברירת מחדל: C:\KioskApp\KioskApp.exe
#>
param(
    [string]$KioskUsername = "kiosk-user",
    [string]$KioskAppPath = "C:\KioskApp\KioskApp.exe"
)

$ErrorActionPreference = "Stop"
# הודעות המסך באנגלית: חלון PowerShell 5.1 הרגיל לא מציג עברית כראוי

Write-Host "=== Kiosk lockdown setup for user: $KioskUsername ===" -ForegroundColor Cyan

if (-not (Test-Path $KioskAppPath)) {
    Write-Host "KioskApp.exe not found at: $KioskAppPath" -ForegroundColor Red
    Write-Host "Install the kiosk first (Update-Kiosk.ps1), then run this script again." -ForegroundColor Red
    exit 1
}
if (-not (Test-Path (Join-Path (Split-Path $KioskAppPath) "appsettings.json"))) {
    Write-Host "appsettings.json is missing next to KioskApp.exe - the kiosk will not start. Aborting." -ForegroundColor Red
    exit 1
}

# --- 0. רשת ביטחון: חייב להיות חשבון מנהל אחר עם סיסמה ידועה ---
$me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name.Split('\')[-1]
if ($me -eq $KioskUsername) {
    Write-Host "Run this script from an ADMINISTRATOR account, not from $KioskUsername. Aborting." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "SAFETY CHECK: you are logged in as '$me'." -ForegroundColor Yellow
Write-Host "After the lockdown you will need THIS account's password to do maintenance." -ForegroundColor Yellow
$answer = Read-Host "Do you know the password of '$me'? Type YES to continue"
if ($answer -ne "YES") {
    Write-Host "Stopped. Make sure you know the admin password, then run again." -ForegroundColor Red
    exit 1
}

# --- 1. המשתמש הייעודי (הרשאות רגילות בלבד) ---
# סיסמה חדשה אקראית בכל הרצה - כך לא צריך לזכור את הסיסמה הקודמת של משתמש הקיוסק
# (ההתחברות האוטומטית משתמשת בה, ואף אחד לא מקליד אותה ידנית)
Add-Type -AssemblyName System.Web
$plainPassword = [System.Web.Security.Membership]::GeneratePassword(16, 3)
$securePassword = ConvertTo-SecureString $plainPassword -AsPlainText -Force

$existingUser = Get-LocalUser -Name $KioskUsername -ErrorAction SilentlyContinue
if ($existingUser) {
    Set-LocalUser -Name $KioskUsername -Password $securePassword
    Enable-LocalUser -Name $KioskUsername
    Write-Host "User $KioskUsername already exists - password was reset." -ForegroundColor Yellow
} else {
    New-LocalUser -Name $KioskUsername -Password $securePassword -PasswordNeverExpires -UserMayNotChangePassword `
        -FullName "Library kiosk" -Description "Dedicated user for KioskApp only" | Out-Null
    Add-LocalGroupMember -Group "Users" -Member $KioskUsername
    Write-Host "Created user $KioskUsername (standard user, NOT administrator)." -ForegroundColor Green
}
# משתמש קיים שהיה בטעות בקבוצת המנהלים - מוציאים אותו משם
Remove-LocalGroupMember -Group "Administrators" -Member $KioskUsername -ErrorAction SilentlyContinue

$plainForUse = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword))
$credential = New-Object System.Management.Automation.PSCredential("$env:COMPUTERNAME\$KioskUsername", $securePassword)

# בדיקה שהסיסמה נכונה - סיסמה שגויה בהתחברות האוטומטית = מסך כניסה במקום קיוסק
Add-Type -AssemblyName System.DirectoryServices.AccountManagement
$ctx = New-Object System.DirectoryServices.AccountManagement.PrincipalContext([System.DirectoryServices.AccountManagement.ContextType]::Machine)
if (-not $ctx.ValidateCredentials($KioskUsername, $plainForUse)) {
    Write-Host "The password of $KioskUsername is not correct. Aborting (nothing was changed yet)." -ForegroundColor Red
    exit 1
}

# --- 2. פרופיל המשתמש (נוצר בכניסה הראשונה; כאן יוצרים אותו בלי להתנתק) ---
$sid = (Get-LocalUser -Name $KioskUsername).SID.Value
$userProfile = Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid }
if (-not $userProfile) {
    Write-Host "Creating the user profile (first-time sign-in in the background)..." -ForegroundColor Cyan
    try {
        Start-Process -FilePath "cmd.exe" -ArgumentList "/c exit" -Credential $credential -LoadUserProfile `
            -WorkingDirectory $env:SystemRoot -WindowStyle Hidden -Wait
    } catch {
        Write-Host "Could not sign in as $KioskUsername - is the password correct? $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    $userProfile = Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid }
}
if (-not $userProfile) { throw "Profile for $KioskUsername was not created." }
$hivePath = Join-Path $userProfile.LocalPath "NTUSER.DAT"

# --- 3. הגדרות של משתמש הקיוסק בלבד (טוענים את ה-registry שלו זמנית) ---
$hiveName = "KioskUserTemp"
$hiveRoot = "Registry::HKEY_USERS\$hiveName"
$loadedHere = $false
if (-not (Test-Path "Registry::HKEY_USERS\$sid")) {
    reg load "HKU\$hiveName" "$hivePath" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not load the registry of $KioskUsername (is that user signed in right now?)." }
    $loadedHere = $true
} else {
    $hiveRoot = "Registry::HKEY_USERS\$sid"
}

try {
    function Set-Value($path, $name, $value, $type) {
        if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
        Set-ItemProperty -Path $path -Name $name -Value $value -Type $type
    }

    # KioskApp במקום שולחן העבודה - רק למשתמש הזה
    Set-Value "$hiveRoot\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" "Shell" $KioskAppPath String

    # Ctrl+Alt+Delete: בלי מנהל משימות, נעילה ושינוי סיסמה (נשארים "יציאה" וכיבוי)
    $sys = "$hiveRoot\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    Set-Value $sys "DisableTaskMgr" 1 DWord
    Set-Value $sys "DisableLockWorkstation" 1 DWord
    Set-Value $sys "DisableChangePassword" 1 DWord

    # בלי חלון "הפעלה" (Win+R) ובלי הפעלה אוטומטית מ-USB/דיסק
    $exp = "$hiveRoot\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
    Set-Value $exp "NoRun" 1 DWord
    Set-Value $exp "NoDriveTypeAutoRun" 0xFF DWord

    Write-Host "Kiosk restrictions applied to $KioskUsername only." -ForegroundColor Green
}
finally {
    if ($loadedHere) {
        [gc]::Collect()
        Start-Sleep -Seconds 1
        reg unload "HKU\$hiveName" | Out-Null
    }
}

# --- 4. התחברות אוטומטית, ו-Shell רגיל לכל המחשב (מתקן גם התקנה ישנה) ---
$winlogon = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty -Path $winlogon -Name "Shell" -Value "explorer.exe" -Type String
Set-ItemProperty -Path $winlogon -Name "AutoAdminLogon" -Value "1" -Type String
Set-ItemProperty -Path $winlogon -Name "DefaultUserName" -Value $KioskUsername -Type String
Set-ItemProperty -Path $winlogon -Name "DefaultDomainName" -Value $env:COMPUTERNAME -Type String
Set-ItemProperty -Path $winlogon -Name "DefaultPassword" -Value $plainForUse -Type String
Remove-Variable plainForUse

# ניקוי שאריות מהגרסה הקודמת של הסקריפט (הגבלות שחלו על כל המחשב)
$oldSys = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
foreach ($n in @("DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword", "HideFastUserSwitching")) {
    Remove-ItemProperty -Path $oldSys -Name $n -ErrorAction SilentlyContinue
}
Remove-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout" -Name "Scancode Map" -ErrorAction SilentlyContinue

Write-Host "Auto-logon to $KioskUsername enabled." -ForegroundColor Green
Write-Host ""
Write-Host "=== DONE. Restart the computer to start the kiosk. ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "MAINTENANCE ACCESS (write this down):" -ForegroundColor Yellow
Write-Host "  1. Ctrl+Alt+Delete -> Sign out" -ForegroundColor Yellow
Write-Host "  2. Immediately press and HOLD the Shift key until the sign-in screen appears" -ForegroundColor Yellow
Write-Host "  3. Choose '$me' and sign in with its password - normal Windows desktop" -ForegroundColor Yellow
Write-Host "  To remove the lockdown completely: Remove-KioskLockdown.ps1" -ForegroundColor Yellow

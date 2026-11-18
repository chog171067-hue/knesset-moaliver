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
    [string]$KioskAppPath = "C:\KioskApp\KioskApp.exe",
    # תיקיות נוספות שמשתמש הקיוסק יכול רק לקרוא (למשל תיקיות התקנה של התוכנות המאושרות)
    [string[]]$ReadOnlyFolders = @("C:\KioskSource", "C:\$([string]::new([char[]]@(0x05D0,0x05D5,0x05E6,0x05E8,0x05D9,0x05D0)))")
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

# שומרים את הסיסמה החדשה להתחברות האוטומטית מיד - כדי שגם אם שלב מאוחר יותר ייכשל,
# הסיסמה השמורה תתאים לסיסמה של המשתמש (אחרת: מסך בחירת משתמשים במקום קיוסק)
$winlogon = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty -Path $winlogon -Name "DefaultUserName" -Value $KioskUsername -Type String
Set-ItemProperty -Path $winlogon -Name "DefaultDomainName" -Value $env:COMPUTERNAME -Type String
Set-ItemProperty -Path $winlogon -Name "DefaultPassword" -Value $plainForUse -Type String

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

# כוננים פנימיים במחשב (DriveType 3 = דיסק קבוע). דיסק-און-קי הוא DriveType 2 - לא כלול.
$fixedDrives = @(Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" | ForEach-Object { $_.DeviceID })
Write-Host "Internal drives: $($fixedDrives -join ', ')" -ForegroundColor Cyan

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
    # בלי "יציאה" ב-Ctrl+Alt+Delete: ההתחברות האוטומטית פועלת רק בהדלקה, ולכן משתמש
    # שהיה מתנתק היה תוקע את העמדה במסך הכניסה של Windows. נשארים כיבוי/הפעלה מחדש -
    # שניהם חוזרים לקיוסק. (כפתור "יציאה למחשב" בקיוסק מתנתק בקוד, לא דרך התפריט.)
    Set-Value $exp "NoLogoff" 1 DWord
    # בלי כפתור כיבוי/שינה/הפעלה מחדש ב-Ctrl+Alt+Delete: משתמש בספרייה לא יכבה או ירדים
    # את העמדה. כפתור ההדלקה הפיזי של המחשב ממשיך לכבות אותו כרגיל (לצוות בסוף היום).
    Set-Value $exp "NoClose" 1 DWord
    # חלונות "פתח"/"שמירה בשם" בתוך התוכנות (למשל שמירת עמוד כ-PDF באוצריא) הם דרך
    # לדפדף בקבצי המחשב ולהפעיל תוכנות. למשתמש הקיוסק בלבד:
    # - כל הכוננים הפנימיים (C, וגם D/E וכו' אם יש) מוסתרים וחסומים בדפדוף. התוכנות עצמן
    #   ממשיכות לקרוא מהם - החסימה היא רק בחלונות הקבצים.
    # - שמירה אפשרית לדיסק-און-קי (כוננים נשלפים לא חסומים)
    # - בלי תפריט לחיצה ימנית (שממנו אפשר להפעיל/למחוק/לשנות שם)
    $driveMask = 0
    foreach ($d in $fixedDrives) { $driveMask = $driveMask -bor (1 -shl ([int][char]$d[0] - [int][char]'A')) }
    Set-Value $exp "NoDrives" $driveMask DWord
    Set-Value $exp "NoViewOnDrive" $driveMask DWord
    Set-Value $exp "NoViewContextMenu" 1 DWord

    Write-Host "Kiosk restrictions applied to $KioskUsername only." -ForegroundColor Green
}
finally {
    if ($loadedHere) {
        [gc]::Collect()
        Start-Sleep -Seconds 1
        reg unload "HKU\$hiveName" | Out-Null
    }
}

# --- 3ב. הרשאות קבצים: ההגנה האמיתית ---
# הגבלות התצוגה (NoViewOnDrive וכו') רק מסתירות; מי שמתעקש מגיע לקבצים בדרכים אחרות.
# תיקיות שנוצרות ישירות ב-C פתוחות ב-Windows לשינוי ולמחיקה ע"י כל משתמש - ולכן משתמש
# בספרייה יכול היה למחוק את הקיוסק או ספרים. כאן הן הופכות לקריאה בלבד למשתמשים רגילים.
function Protect-Folder($folder) {
    if (-not (Test-Path $folder)) { Write-Host "  (skipped, not found: $folder)" -ForegroundColor DarkGray; return }
    # מערכת ומנהלים - מלא; משתמשים רגילים - קריאה והפעלה בלבד. בלי ירושה מ-C.
    icacls "$folder" /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-32-545:(OI)(CI)RX" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        # לא עוצרים את כל הסקריפט בגלל תיקייה אחת - הנעילה עצמה חשובה יותר
        Write-Host "  WARNING: could not set permissions on $folder - continuing" -ForegroundColor Yellow
        return
    }
    # כל מה שבתוכה - יורש מהתיקייה (מבטל הרשאות כתיבה ישנות שהוגדרו על קבצים בודדים)
    icacls "$folder\*" /reset /T /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "  WARNING: some files inside $folder kept their old permissions" -ForegroundColor Yellow }
    Write-Host "  Read-only for standard users: $folder" -ForegroundColor Green
}
Write-Host "Protecting folders (standard users can only read):" -ForegroundColor Cyan
$kioskDir = Split-Path $KioskAppPath
Protect-Folder $kioskDir
foreach ($f in $ReadOnlyFolders) { Protect-Folder $f }

# הקיוסק כותב יומן - הרשאת שינוי לקובץ הזה בלבד
$logFile = Join-Path $kioskDir "kiosk-app.log"
if (-not (Test-Path $logFile)) { New-Item -ItemType File -Path $logFile | Out-Null }
icacls "$logFile" /grant "*${sid}:M" | Out-Null

# כוננים פנימיים נוספים (D:, E: וכו'): ב-Windows כל משתמש יכול לשנות ולמחוק בהם כברירת מחדל.
# חוסמים כתיבה/מחיקה למשתמש הקיוסק בלבד, בכל הכונן (קריאה נשארת - למקרה שתוכנה קוראת משם).
# כונן המערכת (C) מוגן ע"י Windows עצמו, והתיקיות שלנו בו - למעלה.
foreach ($d in $fixedDrives) {
    if ($d -eq $env:SystemDrive) { continue }
    icacls "$d\" /deny "*${sid}:(OI)(CI)(W,D,DC)" | Out-Null
    Write-Host "  $KioskUsername cannot write/delete on drive $d" -ForegroundColor Green
}

# התיקייה הציבורית של Windows פתוחה לכתיבה לכל המשתמשים - חוסמים בה כתיבה/מחיקה למשתמש הקיוסק
icacls "C:\Users\Public" /deny "*${sid}:(OI)(CI)(W,D,DC)" | Out-Null
Write-Host "  $KioskUsername cannot write to C:\Users\Public" -ForegroundColor Green

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
foreach ($n in @("DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword")) {
    Remove-ItemProperty -Path $oldSys -Name $n -ErrorAction SilentlyContinue
}

# העמדה לא נרדמת ולא מכבה מסך/דיסק מעצמה (במחובר לחשמל) - כדי שתהיה תמיד זמינה
powercfg /change standby-timeout-ac 0 | Out-Null
powercfg /change hibernate-timeout-ac 0 | Out-Null
Write-Host "Sleep and hibernate on AC power disabled." -ForegroundColor Green

# אחרי הפסקת חשמל Windows עלול להציג בהדלקה "תיקון אוטומטי" / "Windows לא נסגר כראוי"
# ולחכות שמישהו יבחר אפשרות - והעמדה תקועה. כאן מגדירים לו לעלות כרגיל בכל מקרה.
bcdedit /set "{current}" bootstatuspolicy ignoreallfailures | Out-Null
bcdedit /set "{current}" recoveryenabled No | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Windows will start normally after a power failure (no repair screens)." -ForegroundColor Green
} else {
    Write-Host "WARNING: could not change the boot recovery settings" -ForegroundColor Yellow
}

# בלי "החלפת משתמש": היא משאירה את הקיוסק פתוח ברקע ומציגה מסך כניסה, וכדי לחזור
# לקיוסק משם צריך את סיסמת משתמש הקיוסק (שאיש לא יודע) - העמדה הייתה נתקעת.
# Windows מאפשר להסתיר אותה רק לכל המחשב; למנהל זה לא מפריע.
if (-not (Test-Path $oldSys)) { New-Item -Path $oldSys -Force | Out-Null }
Set-ItemProperty -Path $oldSys -Name "HideFastUserSwitching" -Value 1 -Type DWord
Remove-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout" -Name "Scancode Map" -ErrorAction SilentlyContinue

Write-Host "Auto-logon to $KioskUsername enabled." -ForegroundColor Green

# --- 5. שמירה על ההתחברות האוטומטית לאורך זמן ---
# Windows מחליף את DefaultUserName למשתמש האחרון שנכנס. אחרי כניסת מנהל לתחזוקה, ההפעלה
# הבאה ניסתה להיכנס למנהל עם סיסמת הקיוסק, נכשלה, והציגה מסך בחירת משתמשים. משימה
# מתוזמנת (בהרשאות מערכת) מחזירה את ההגדרות למשתמש הקיוסק בכל הדלקה ובכל כניסה של משתמש.
$taskName = "KioskAutoLogonGuard"
$wl = "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
$cmd = "reg add `"$wl`" /v DefaultUserName /t REG_SZ /d `"$KioskUsername`" /f & " +
       "reg add `"$wl`" /v DefaultDomainName /t REG_SZ /d `"$env:COMPUTERNAME`" /f & " +
       "reg add `"$wl`" /v AutoAdminLogon /t REG_SZ /d 1 /f"
$action = New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/c $cmd"
$triggers = @((New-ScheduledTaskTrigger -AtStartup), (New-ScheduledTaskTrigger -AtLogOn))
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers -Principal $principal `
    -Settings $settings -Description "Keeps the computer auto-logging on to the library kiosk user" -Force | Out-Null
Write-Host "Auto-logon guard task '$taskName' installed (runs at every startup and sign-in)." -ForegroundColor Green

# --- 6. "יציאה למחשב" ממסך הקיוסק (למשתמשי קיוסק מורשים) ---
# משתמש הקיוסק לא מורשה לכבות את ההתחברות האוטומטית בעצמו (וטוב שכך). המשימה הזו רצה
# בהרשאות מערכת, ומותר למשתמש הקיוסק רק להפעיל אותה (לא לשנות אותה): היא מכבה את
# ההתחברות האוטומטית לחצי דקה - מספיק כדי שאחרי ההתנתקות יופיע מסך הכניסה של Windows -
# ואז מחזירה אותה, כך שהפעלה מחדש של המחשב תמיד חוזרת לקיוסק.
$maintTask = "KioskMaintenanceSignIn"
# בסוף: אם תוך 5 דקות אף אחד לא נכנס ל-Windows (מורשה לחץ "יציאה למחשב" והלך) - הפעלה
# מחדש, וחזרה לקיוסק. quser מחזיר שגיאה כשאין אף משתמש מחובר.
$maintCmd = "reg add `"$wl`" /v AutoAdminLogon /t REG_SZ /d 0 /f & " +
            "ping -n 31 127.0.0.1 >nul & " +
            "reg add `"$wl`" /v AutoAdminLogon /t REG_SZ /d 1 /f & " +
            "ping -n 271 127.0.0.1 >nul & " +
            "quser >nul 2>&1 || shutdown /r /t 0 /c `"Back to the library kiosk`""
$svc = New-Object -ComObject "Schedule.Service"
$svc.Connect()
$def = $svc.NewTask(0)
$def.RegistrationInfo.Description = "Lets an authorized kiosk user reach the Windows sign-in screen (auto-logon off for 30 seconds)"
$def.Settings.Enabled = $true
$def.Settings.AllowDemandStart = $true
$def.Settings.DisallowStartIfOnBatteries = $false
$def.Settings.StopIfGoingOnBatteries = $false
$def.Settings.ExecutionTimeLimit = "PT10M"
$def.Principal.RunLevel = 1
$maintAction = $def.Actions.Create(0)
$maintAction.Path = "cmd.exe"
$maintAction.Arguments = "/c $maintCmd"
# הרשאות: מערכת ומנהלים - מלאות; משתמש הקיוסק - קריאה והפעלה בלבד
$sddl = "D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;$sid)"
$svc.GetFolder("\").RegisterTaskDefinition($maintTask, $def, 6, "SYSTEM", $null, 5, $sddl) | Out-Null
Write-Host "Maintenance sign-in task '$maintTask' installed (used by the kiosk 'exit to computer' button)." -ForegroundColor Green

# --- 7. קיצור דרך "חזרה לקיוסק" על שולחן העבודה של המנהל ---
# ההתחברות האוטומטית פועלת רק בהדלקה (לא אחרי התנתקות) - לכן הקיצור מפעיל מחדש את המחשב.
$adminDesktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $adminDesktop ([string]::new([char[]]@(0x05D7,0x05D6,0x05E8,0x05D4,0x0020,0x05DC,0x05E7,0x05D9,0x05D5,0x05E1,0x05E7)) + ".lnk")
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcutPath)
$lnk.TargetPath = "$env:SystemRoot\System32\shutdown.exe"
$lnk.Arguments = "/r /t 0"
$lnk.IconLocation = "$env:SystemRoot\System32\shell32.dll,44"
$lnk.Description = "Restart the computer back into the library kiosk"
$lnk.Save()
Write-Host "Desktop shortcut 'back to kiosk' created for '$me' (restarts the computer)." -ForegroundColor Green
Write-Host ""
Write-Host "=== DONE. Restart the computer to start the kiosk. ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "MAINTENANCE ACCESS (write this down):" -ForegroundColor Yellow
Write-Host "  Usual way: an authorized kiosk account -> 'exit to computer' button -> sign in as '$me'" -ForegroundColor Yellow
Write-Host "  Without a kiosk account: short press on the computer's physical power button (shuts down)," -ForegroundColor Yellow
Write-Host "     turn it on again and HOLD the Shift key while Windows starts, until the sign-in screen appears" -ForegroundColor Yellow
Write-Host "  Back to the kiosk: the desktop shortcut (restarts the computer)" -ForegroundColor Yellow
Write-Host "  To remove the lockdown completely: Remove-KioskLockdown.ps1" -ForegroundColor Yellow

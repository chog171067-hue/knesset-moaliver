#Requires -RunAsAdministrator
<#
.SYNOPSIS
    מגדיר את מחשב הספרייה כעמדת קיוסק נעולה: יוצר משתמש ייעודי בהרשאות רגילות,
    מגדיר התחברות אוטומטית אליו, וקובע את KioskApp כ-Shell שלו (במקום explorer.exe).

.DESCRIPTION
    הרצה חד-פעמית, כמנהל מערכת (Run as Administrator), על המחשב הפיזי בספרייה.
    לפני ההרצה: יש לבנות ולפרוס את KioskApp (ראו kiosk-app/README.md) לנתיב קבוע -
    ברירת המחדל כאן היא C:\KioskApp\KioskApp.exe.

    ⚠️ הערות חשובות:
    - הסקריפט קובע Shell ברמת המחשב כולו (HKLM), לא רק למשתמש kiosk-user - זה
      פשוט ואמין יותר מטעינת registry hive של משתמש ספציפי, ומתאים למחשב שכל
      ייעודו הוא הקיוסק (אף אחד לא אמור להתחבר אליו עם משתמש אחר בכל מקרה).
      אם בכל זאת יש צורך שמשתמשים אחרים (למשל הצוות הטכני) יתחברו עם ה-Shell
      הרגיל של Windows, יש לתאם זאת בנפרד (למשל ע"י יצירת משתמש admin נוסף
      ולוודא שה-Shell שנקבע כאן לא חל עליו - זה דורש טעינת hive פר-משתמש,
      ראו הערה בתחתית הקובץ).
    - כדי לחזור למצב הרגיל (Windows רגיל, לא קיוסק) - הריצו את
      Remove-KioskLockdown.ps1 שבתיקייה הזו.
    - מומלץ מאוד לבדוק את כל התהליך פעם אחת על מחשב לא-קריטי לפני ההתקנה על
      עמדת הספרייה בפועל.

.PARAMETER KioskUsername
    שם המשתמש הייעודי לעמדה. ברירת מחדל: kiosk-user

.PARAMETER KioskPassword
    סיסמת המשתמש הייעודי (SecureString). אם לא מסופקת, הסקריפט ייצור סיסמה אקראית
    ויציג אותה בסוף - שמרו אותה במקום בטוח (תידרש אם תרצו להתחבר למשתמש הזה ידנית).

.PARAMETER KioskAppPath
    הנתיב המלא ל-KioskApp.exe לאחר הפריסה. ברירת מחדל: C:\KioskApp\KioskApp.exe
#>
param(
    [string]$KioskUsername = "kiosk-user",
    [System.Security.SecureString]$KioskPassword,
    [string]$KioskAppPath = "C:\KioskApp\KioskApp.exe"
)

$ErrorActionPreference = "Stop"

Write-Host "=== הגדרת עמדת קיוסק - $KioskUsername ===" -ForegroundColor Cyan

if (-not (Test-Path $KioskAppPath)) {
    Write-Warning "לא נמצא קובץ ההרצה בנתיב: $KioskAppPath"
    Write-Warning "יש לבנות ולפרוס את KioskApp קודם (ראו kiosk-app/README.md), ואז להריץ את הסקריפט הזה שוב."
    exit 1
}

# --- 1. יצירת המשתמש הייעודי (הרשאות רגילות בלבד - לא Administrator) ---
$existingUser = Get-LocalUser -Name $KioskUsername -ErrorAction SilentlyContinue
if ($existingUser) {
    Write-Host "המשתמש $KioskUsername כבר קיים - מדלגים על יצירתו." -ForegroundColor Yellow
} else {
    if (-not $KioskPassword) {
        Add-Type -AssemblyName System.Web
        $plainPassword = [System.Web.Security.Membership]::GeneratePassword(16, 4)
        $KioskPassword = ConvertTo-SecureString $plainPassword -AsPlainText -Force
        Write-Host "נוצרה סיסמה אקראית עבור $KioskUsername : $plainPassword" -ForegroundColor Magenta
        Write-Host "שמרו את הסיסמה הזו במקום בטוח!" -ForegroundColor Magenta
    }

    New-LocalUser -Name $KioskUsername -Password $KioskPassword -PasswordNeverExpires -UserMayNotChangePassword `
        -FullName "עמדת קיוסק ספרייה" -Description "משתמש ייעודי להרצת KioskApp בלבד"

    # במפורש בקבוצת "Users" הרגילה - לא "Administrators". זה מה שמונע התקנת
    # תוכנות/שינוי הגדרות מערכת גם אם מישהו יצליח לצאת רגעית מהאפליקציה.
    Add-LocalGroupMember -Group "Users" -Member $KioskUsername
    Write-Host "המשתמש $KioskUsername נוצר בהרשאות סטנדרטיות (לא מנהל)." -ForegroundColor Green
}

# --- 2. התחברות אוטומטית ל-kiosk-user בכל אתחול (בלי מסך login רגיל של Windows) ---
$winlogonPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty -Path $winlogonPath -Name "AutoAdminLogon" -Value "1" -Type String
Set-ItemProperty -Path $winlogonPath -Name "DefaultUserName" -Value $KioskUsername -Type String
Set-ItemProperty -Path $winlogonPath -Name "DefaultDomainName" -Value $env:COMPUTERNAME -Type String
if ($KioskPassword) {
    $plainForRegistry = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($KioskPassword))
    Set-ItemProperty -Path $winlogonPath -Name "DefaultPassword" -Value $plainForRegistry -Type String
    Remove-Variable plainForRegistry
}
Write-Host "התחברות אוטומטית הוגדרה." -ForegroundColor Green

# --- 3. KioskApp כ-Shell במקום explorer.exe (רמת מחשב - ראו הערה בראש הקובץ) ---
Set-ItemProperty -Path $winlogonPath -Name "Shell" -Value $KioskAppPath -Type String
Write-Host "KioskApp הוגדר כ-Shell במקום Windows Explorer." -ForegroundColor Green

# --- 4. הקשחת מקשים/תפריטים (מדיניות שקבילה כ-Group Policy, מוגדרות כאן ישירות ב-registry) ---
$systemPolicyPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
New-Item -Path $systemPolicyPath -Force | Out-Null
Set-ItemProperty -Path $systemPolicyPath -Name "DisableTaskMgr" -Value 1 -Type DWord
Set-ItemProperty -Path $systemPolicyPath -Name "DisableLockWorkstation" -Value 1 -Type DWord
Set-ItemProperty -Path $systemPolicyPath -Name "DisableChangePassword" -Value 1 -Type DWord
Set-ItemProperty -Path $systemPolicyPath -Name "HideFastUserSwitching" -Value 1 -Type DWord
Write-Host "Task Manager, נעילת מסך, שינוי סיסמה והחלפת משתמש הוסתרו מתפריט Ctrl+Alt+Del." -ForegroundColor Green

$explorerPolicyPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer"
New-Item -Path $explorerPolicyPath -Force | Out-Null
# חסימת Autorun/Autoplay לכל סוגי הכוננים (כולל USB) - מונע הרצה אוטומטית מהתקן חיצוני
Set-ItemProperty -Path $explorerPolicyPath -Name "NoDriveTypeAutoRun" -Value 0xFF -Type DWord

# --- 5. השבתת מקש Windows (Scancode Map - טכניקה תקנית של Windows, לא כלי צד ג') ---
# מרוקן את שני מקשי ה-Windows (שמאל/ימין) בלי להשפיע על שאר המקלדת. תופס אחרי אתחול.
$scancodeMapPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout"
$scancodeMapBytes = [byte[]](0,0,0,0, 0,0,0,0, 3,0,0,0, 0,0,0x5B,0xE0, 0,0,0x5C,0xE0, 0,0,0,0)
Set-ItemProperty -Path $scancodeMapPath -Name "Scancode Map" -Value $scancodeMapBytes -Type Binary
Write-Host "מקש Windows הושבת (ידרוש אתחול כדי להיכנס לתוקף)." -ForegroundColor Green

Write-Host ""
Write-Host "=== ההגדרה הושלמה. יש להפעיל את המחשב מחדש כדי שהכל ייכנס לתוקף. ===" -ForegroundColor Cyan
Write-Host "לפני האתחול הראשון: ודאו שיש לכם דרך חזרה למקרה שמשהו לא עובד כמצופה -" -ForegroundColor Yellow
Write-Host "ראו את סעיף 'איך יוצאים ממצב קיוסק אם צריך' ב-kiosk-app/DEPLOYMENT.md" -ForegroundColor Yellow

#Requires -RunAsAdministrator
<#
.SYNOPSIS
    מבטל את נעילת הקיוסק (Setup-KioskUser.ps1) ומחזיר את המחשב ל-Windows רגיל.

.DESCRIPTION
    - מבטל את ההתחברות האוטומטית ומוחק את הסיסמה השמורה שלה.
    - מוודא שה-Shell של המחשב הוא explorer.exe (מתקן גם התקנה ישנה שנעלה את כל המחשב).
    - מסיר את ההגבלות מההגדרות של משתמש הקיוסק.
    - לא מוחק את משתמש הקיוסק, אלא אם מריצים עם -RemoveUser.

    הרצה (PowerShell כמנהל):
      powershell -ExecutionPolicy Bypass -File C:\KioskSource\kiosk-app\setup\Remove-KioskLockdown.ps1
#>
param(
    [string]$KioskUsername = "kiosk-user",
    [switch]$RemoveUser
)

$ErrorActionPreference = "Stop"
# הודעות המסך באנגלית: חלון PowerShell 5.1 הרגיל לא מציג עברית כראוי

Write-Host "=== Removing kiosk lockdown ===" -ForegroundColor Cyan

# --- הגדרות ברמת המחשב ---
$winlogon = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty -Path $winlogon -Name "Shell" -Value "explorer.exe" -Type String
Set-ItemProperty -Path $winlogon -Name "AutoAdminLogon" -Value "0" -Type String
Remove-ItemProperty -Path $winlogon -Name "DefaultPassword" -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "KioskAutoLogonGuard" -Confirm:$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "KioskMaintenanceSignIn" -Confirm:$false -ErrorAction SilentlyContinue
Write-Host "Auto-logon disabled, computer shell is explorer.exe." -ForegroundColor Green

# שאריות מהגרסה הישנה של סקריפט הנעילה (הגבלות לכל המחשב)
$oldSys = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
foreach ($n in @("DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword", "HideFastUserSwitching")) {
    Remove-ItemProperty -Path $oldSys -Name $n -ErrorAction SilentlyContinue
}
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" -Name "NoDriveTypeAutoRun" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout" -Name "Scancode Map" -ErrorAction SilentlyContinue

# --- הגדרות של משתמש הקיוסק ---
$user = Get-LocalUser -Name $KioskUsername -ErrorAction SilentlyContinue
if ($user) {
    $sid = $user.SID.Value
    $userProfile = Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid }
    if ($userProfile) {
        $hiveName = "KioskUserTemp"
        $hiveRoot = "Registry::HKEY_USERS\$hiveName"
        $loadedHere = $false
        if (Test-Path "Registry::HKEY_USERS\$sid") {
            $hiveRoot = "Registry::HKEY_USERS\$sid"
        } else {
            reg load "HKU\$hiveName" (Join-Path $userProfile.LocalPath "NTUSER.DAT") | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Could not load the registry of $KioskUsername (sign that user out first)." }
            $loadedHere = $true
        }
        try {
            Remove-ItemProperty -Path "$hiveRoot\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "Shell" -ErrorAction SilentlyContinue
            $sys = "$hiveRoot\Software\Microsoft\Windows\CurrentVersion\Policies\System"
            foreach ($n in @("DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword")) {
                Remove-ItemProperty -Path $sys -Name $n -ErrorAction SilentlyContinue
            }
            $exp = "$hiveRoot\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
            foreach ($n in @("NoRun", "NoDriveTypeAutoRun", "NoLogoff", "NoClose")) {
                Remove-ItemProperty -Path $exp -Name $n -ErrorAction SilentlyContinue
            }
            Write-Host "Restrictions removed from $KioskUsername." -ForegroundColor Green
        }
        finally {
            if ($loadedHere) {
                [gc]::Collect()
                Start-Sleep -Seconds 1
                reg unload "HKU\$hiveName" | Out-Null
            }
        }
    }

    if ($RemoveUser) {
        Remove-LocalUser -Name $KioskUsername
        Write-Host "User $KioskUsername deleted." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=== Done. Restart the computer. ===" -ForegroundColor Cyan

#Requires -RunAsAdministrator
<#
.SYNOPSIS
    מבטל את כל השינויים שביצע Setup-KioskUser.ps1 - מחזיר את המחשב להתנהגות Windows
    רגילה (Explorer כ-Shell, בלי התחברות אוטומטית, בלי הגבלות מקלדת/תפריטים).

.DESCRIPTION
    להריץ כמנהל מערכת. לא מוחק את משתמש kiosk-user עצמו (ברירת מחדל) - כדי למחוק
    גם אותו, הריצו עם -RemoveUser.

    שימושי לבדיקות לפני התקנה בפועל, ולחזרה מהירה למצב רגיל אם נדרש לתחזק את
    המחשב (למשל התקנת עדכונים, שינויי הגדרות).
#>
param(
    [string]$KioskUsername = "kiosk-user",
    [switch]$RemoveUser
)

$ErrorActionPreference = "Stop"

Write-Host "=== ביטול הגדרות עמדת קיוסק ===" -ForegroundColor Cyan

$winlogonPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty -Path $winlogonPath -Name "Shell" -Value "explorer.exe" -Type String
Set-ItemProperty -Path $winlogonPath -Name "AutoAdminLogon" -Value "0" -Type String
Remove-ItemProperty -Path $winlogonPath -Name "DefaultPassword" -ErrorAction SilentlyContinue
Write-Host "Shell הוחזר ל-explorer.exe, התחברות אוטומטית בוטלה." -ForegroundColor Green

$systemPolicyPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
foreach ($name in @("DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword", "HideFastUserSwitching")) {
    Remove-ItemProperty -Path $systemPolicyPath -Name $name -ErrorAction SilentlyContinue
}

$explorerPolicyPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer"
Remove-ItemProperty -Path $explorerPolicyPath -Name "NoDriveTypeAutoRun" -ErrorAction SilentlyContinue

$scancodeMapPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout"
Remove-ItemProperty -Path $scancodeMapPath -Name "Scancode Map" -ErrorAction SilentlyContinue
Write-Host "מדיניות תפריטים/מקלדת בוטלה." -ForegroundColor Green

if ($RemoveUser) {
    $existingUser = Get-LocalUser -Name $KioskUsername -ErrorAction SilentlyContinue
    if ($existingUser) {
        Remove-LocalUser -Name $KioskUsername
        Write-Host "המשתמש $KioskUsername נמחק." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=== בוטל בהצלחה. יש להפעיל את המחשב מחדש. ===" -ForegroundColor Cyan

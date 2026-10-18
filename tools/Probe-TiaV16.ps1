#Requires -Version 5.1
<#
.SYNOPSIS
    Probes whether TIA Portal V16 can run headless on this machine.
.DESCRIPTION
    Minimal, non-destructive check performed BEFORE any import work:
      1. Assembly identity of the V16 Openness DLL.
      2. TiaPortal WithoutUserInterface startup and the ProcessId it reports.
      3. Whether the required "Siemens TIA Openness" group membership is present.
    Creates no project and modifies nothing.

    Exit code 0 = TIA started headless.
    Exit code 2 = TIA did not start (captured exception is printed verbatim).
#>
[CmdletBinding()]
param(
    [string]$TiaInstallPath = 'C:\Program Files\Siemens\Automation\Portal V16',
    [string]$Version = 'V16'
)

$ErrorActionPreference = 'Stop'

$dll = Join-Path $TiaInstallPath "PublicAPI\$Version\Siemens.Engineering.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    Write-Error "Openness assembly not found: $dll"
    exit 1
}

Write-Output '=== 1. Assembly identity ==='
$assembly = [System.Reflection.Assembly]::LoadFrom($dll)
Write-Output "  $($assembly.FullName)"
Write-Output "  Location: $dll"

Write-Output ''
Write-Output '=== 2. Local group membership ==='
$group = Get-LocalGroup -SID 'S-1-5-32-544' -ErrorAction SilentlyContinue
if ($null -eq $group) {
    $group = Get-LocalGroup -Name 'Administrators' -ErrorAction SilentlyContinue
}
$current = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
Write-Output "  Current identity : $current"
$isAdmin = ([System.Security.Principal.WindowsPrincipal] `
    [System.Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [System.Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Output "  Is administrator  : $isAdmin"
Write-Output '  Note: Openness only requires membership in "Siemens TIA Openness",'
Write-Output '        but Administrators also satisfy the TIA access check.'

Write-Output ''
Write-Output '=== 3. TiaPortal WithoutUserInterface startup ==='
$tia = $null
try {
    $tia = New-Object Siemens.Engineering.TiaPortal ([Siemens.Engineering.TiaPortalMode]::WithoutUserInterface)
    $processId = $tia.GetType().GetProperty('ProcessId')
    if ($null -eq $processId) {
        Write-Output '  Started. (This build does not expose ProcessId.)'
    } else {
        Write-Output "  Started. TIA process id: $($processId.GetValue($tia, $null))"
    }
    $running = $tia.GetType().GetProperty('IsRunning')
    if ($null -ne $running) {
        Write-Output "  IsRunning: $($running.GetValue($tia, $null))"
    }
    Write-Output ''
    Write-Host 'RESULT: TIA Portal V16 started headless.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Output ''
    Write-Host 'RESULT: TIA Portal V16 did NOT start.' -ForegroundColor Red
    Write-Output ''
    Write-Output '--- verbatim exception ---'
    Write-Output "  Type       : $($_.Exception.GetType().FullName)"
    Write-Output "  Message    : $($_.Exception.Message)"
    if ($_.Exception.InnerException) {
        Write-Output "  Inner type : $($_.Exception.InnerException.GetType().FullName)"
        Write-Output "  Inner msg  : $($_.Exception.InnerException.Message)"
    }
    Write-Output '--- stack trace ---'
    Write-Output $_.ScriptStackTrace
    exit 2
}
finally {
    if ($null -ne $tia) { $tia.Dispose() }
}
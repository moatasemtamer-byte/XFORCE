#Requires -Version 5.1
<#
.SYNOPSIS
    Exports a TIA Portal V16 project (.ap16) to SimaticML so it can be fed to
    `pt2conv fromsimatic` and turned into a TriStation PLCopen XML / .pt2x.

.DESCRIPTION
    Opens the project head-less with TIA Openness and exports, into a folder layout that
    `fromsimatic` scans recursively:
        Blocks\<name>.xml   user blocks (FB/FC/OB/DB)
        Tags\<name>.xml     tag tables
        UDTs\<name>.xml     user data types
    Only the programming-independent SimaticML is written; the block's own
    ProgrammingLanguage (FBD/SCL/LAD) is preserved in the export.

.EXAMPLE
    .\Export-FromTia.ps1 -ProjectFile C:\TIA_Projects\R33\R33\R33.ap16 -OutDir C:\out\r33export
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)][string]$ProjectFile,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$TiaInstallPath = 'C:\Program Files\Siemens\Automation\Portal V16',
    [switch]$Attach,
    [switch]$SkipTags,
    [switch]$SkipUdts
)

$ErrorActionPreference = 'Stop'

# Attach mode exports the project already open in a running TIA Portal instance (read-only; the
# user's session and unsaved work are left untouched). Otherwise a fresh head-less Portal is started,
# which requires no other Portal to be running.
$portalRunning = [bool](Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'Siemens.Automation.Portal' })
if (-not $Attach -and $portalRunning) {
    throw "TIA Portal is already running. Re-run with -Attach to export the open project, or close it."
}

$opennessPath = Join-Path $TiaInstallPath 'PublicAPI\V16\Siemens.Engineering.dll'
if (-not (Test-Path -LiteralPath $opennessPath)) { throw "Openness DLL not found: $opennessPath" }

$probeDirs = @((Split-Path -Parent $opennessPath))
$bin = Join-Path $TiaInstallPath 'Bin\PublicAPI'
if (Test-Path -LiteralPath $bin) {
    $probeDirs += $bin
    foreach ($child in Get-ChildItem -LiteralPath $bin -Directory -Recurse -ErrorAction SilentlyContinue) {
        $probeDirs += $child.FullName
    }
}
foreach ($dir in $probeDirs) {
    Get-ChildItem -LiteralPath $dir -Filter *.dll -ErrorAction SilentlyContinue | ForEach-Object {
        try { [System.Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch { }
    }
}
Add-Type -Path $opennessPath

function Get-PlcSoftware($item) {
    $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance
    foreach ($it in @($item) + @($item.DeviceItems)) {
        $method = $it.GetType().GetMethod('GetService', $flags)
        if ($null -ne $method) {
            $container = $method.MakeGenericMethod([Siemens.Engineering.HW.Features.SoftwareContainer]).Invoke($it, @())
            if ($null -ne $container -and $null -ne $container.Software) {
                foreach ($software in @($container.Software)) {
                    if ($software -is [Siemens.Engineering.SW.PlcSoftware]) { return $software }
                }
            }
        }
    }
    return $null
}

function Get-AllBlocks($group, $list) {
    foreach ($b in $group.Blocks) { $list.Add($b) | Out-Null }
    foreach ($g in $group.BlockGroups) { Get-AllBlocks $g $list }
}

function Get-AllTagTables($group, $list) {
    foreach ($t in $group.TagTables) { $list.Add($t) | Out-Null }
    foreach ($g in $group.TagTableGroups) { Get-AllTagTables $g $list }
}

function Get-AllTypes($group, $list) {
    foreach ($t in $group.Types) { $list.Add($t) | Out-Null }
    foreach ($g in $group.TypeGroups) { Get-AllTypes $g $list }
}

function New-DirIfMissing($p) { if (-not (Test-Path -LiteralPath $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null } }

$blocksDir = Join-Path $OutDir 'Blocks'
$tagsDir = Join-Path $OutDir 'Tags'
$udtsDir = Join-Path $OutDir 'UDTs'
New-DirIfMissing $blocksDir

Write-Host "Starting TIA Portal V16 (headless)..." -ForegroundColor Cyan
$tiaPortal = $null
$project = $null
$ownsPortal = $false
if ($Attach) {
    $procs = @([Siemens.Engineering.TiaPortal]::GetProcesses())
    if ($procs.Count -eq 0) { throw "No running TIA Portal instance to attach to." }
    Write-Host "Attaching to running TIA Portal..." -ForegroundColor Cyan
    $tiaPortal = $procs[0].Attach()
    $project = @($tiaPortal.Projects)[0]
    if (-not $project) { throw "The running TIA Portal has no project open." }
    Write-Host ("Exported project: {0}" -f $project.Name)
}
else {
    if ([string]::IsNullOrWhiteSpace($ProjectFile)) { throw "-ProjectFile is required unless -Attach is used." }
    $tiaPortal = New-Object Siemens.Engineering.TiaPortal([Siemens.Engineering.TiaPortalMode]::WithoutUserInterface)
    $ownsPortal = $true
}
try {
    if (-not $Attach) {
        Write-Host "Opening project: $ProjectFile"
        $project = $tiaPortal.Projects.Open($ProjectFile)
    }

    $plc = $null
    foreach ($device in $project.Devices) {
        foreach ($item in $device.DeviceItems) {
            $plc = Get-PlcSoftware $item
            if ($null -ne $plc) { break }
        }
        if ($null -ne $plc) { break }
    }
    if (-not $plc) { throw "No PLC software found in the project." }

    $allBlocks = New-Object System.Collections.Generic.List[object]
    Get-AllBlocks $plc.BlockGroup $allBlocks
    Write-Host ("Exporting {0} block(s)..." -f $allBlocks.Count) -ForegroundColor Cyan
    $nBlocks = 0
    foreach ($block in $allBlocks) {
        $lang = '(unknown)'
        try { $lang = [string]$block.ProgrammingLanguage } catch { }
        $target = Join-Path $blocksDir ($block.Name + '.xml')
        try {
            $block.Export([System.IO.FileInfo]::new($target), [Siemens.Engineering.ExportOptions]::WithDefaults)
            $nBlocks++
            Write-Host ("  {0} [{1}]" -f $block.Name, $lang)
        } catch {
            Write-Warning ("  block {0} export failed: {1}" -f $block.Name, $_.Exception.Message)
        }
    }

    if (-not $SkipTags) {
        New-DirIfMissing $tagsDir
        $tables = New-Object System.Collections.Generic.List[object]
        Get-AllTagTables $plc.TagTableGroup $tables
        Write-Host ("Exporting {0} tag table(s)..." -f $tables.Count) -ForegroundColor Cyan
        foreach ($table in $tables) {
            $target = Join-Path $tagsDir ($table.Name + '.xml')
            try {
                $table.Export([System.IO.FileInfo]::new($target), [Siemens.Engineering.ExportOptions]::WithDefaults)
                Write-Host ("  {0}" -f $table.Name)
            } catch {
                Write-Warning ("  tag table {0} export failed: {1}" -f $table.Name, $_.Exception.Message)
            }
        }
    }

    if (-not $SkipUdts) {
        New-DirIfMissing $udtsDir
        $types = New-Object System.Collections.Generic.List[object]
        Get-AllTypes $plc.TypeGroup $types
        Write-Host ("Exporting {0} UDT(s)..." -f $types.Count) -ForegroundColor Cyan
        foreach ($type in $types) {
            $target = Join-Path $udtsDir ($type.Name + '.xml')
            try {
                $type.Export([System.IO.FileInfo]::new($target), [Siemens.Engineering.ExportOptions]::WithDefaults)
            } catch {
                Write-Warning ("  UDT {0} export failed: {1}" -f $type.Name, $_.Exception.Message)
            }
        }
    }

    Write-Host ("Done. {0} block(s) written to {1}" -f $nBlocks, $blocksDir) -ForegroundColor Green
}
finally {
    if ($ownsPortal -and $project) { try { $project.Close() } catch { } }
    if ($tiaPortal) { try { $tiaPortal.Dispose() } catch { } }
}

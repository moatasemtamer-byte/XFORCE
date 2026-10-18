<#
.SYNOPSIS
    Inspects a TIA Portal V16 project through the Openness API and writes an inventory.

.DESCRIPTION
    Reads a .ap16 project and reports, without hand-clicking anything in TIA:
      * every PLC device, its CPU order number and firmware
      * every block: name, number, kind (OB/FB/FC/DB) and programming language
      * every UDT and its members
      * every tag table and its tags (name, type, logical address)
      * per-language and per-object counts, plus a "does this project contain X?" checklist

    Everything it cannot read is reported with the exception text; nothing is skipped silently.

    The project is COPIED before it is opened. The original folder is never opened or modified,
    because PEData.plf is a proprietary binary database and the owner's copy must stay pristine.

.PARAMETER ProjectPath
    The .ap16 file to inspect.

.PARAMETER OutputDir
    Where inventory.json / inventory.md (and, with -Export, the exported goldens) are written.

.PARAMETER Export
    Also export every block, UDT and tag table to <OutputDir>\export.

.PARAMETER AttachToOpen
    Attach to the TIA V16 instance that already has this project open and inspect the loaded
    project in place. Read-only: nothing is modified, saved or closed, so it is safe against a
    live editing session. Use this when the .ap16 is locked because TIA has it loaded.

.PARAMETER UseRunningTia
    Attach to a running TIA V16 instead of starting a new headless instance, but still open the
    project by copying it. Off by default: a new instance cannot disturb work the owner has open.

.EXAMPLE
    .\Inspect-TiaProject.ps1 -ProjectPath 'C:\...\Zanzeta_L01_190722.ap16' -OutputDir 'C:\XforceMsw\goldens\v16' -Export
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [switch]$Export,
    [switch]$UseRunningTia,
    [switch]$AttachToOpen
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- configuration

$TiaRoot    = 'C:\Program Files\Siemens\Automation\Portal V16'
$PublicApi  = Join-Path $TiaRoot 'PublicAPI\V16'
$TiaBin     = Join-Path $TiaRoot 'Bin'
$Dll        = Join-Path $PublicApi 'Siemens.Engineering.dll'
$StageDir   = Join-Path $env:TEMP ("tiaportal-v16-probe-" + [System.Guid]::NewGuid().ToString('N').Substring(0,8))
$CopyRoot   = Join-Path $env:TEMP 'tiaportal-v16-workcopy'

# Candidate attribute names for CPU identity. Probed rather than assumed, so that whatever
# this V16 build exposes is what gets reported.
$CpuAttributes = @(
    'OrderNumber', 'OrderId', 'OrderNumberId', 'DeviceOrderNumber',
    'FirmwareVersion', 'Firmware', 'Version', 'DeviceVersion', 'CatalogNumber', 'Name'
)

# ---------------------------------------------------------------- guard rails

if (-not (Test-Path -LiteralPath $Dll)) { throw "TIA Portal V16 Openness DLL not found at '$Dll'." }
if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "Project not found: '$ProjectPath'." }

$projectFile = (Resolve-Path -LiteralPath $ProjectPath).Path
$projectDir  = Split-Path -Parent $projectFile
if (-not (Test-Path -LiteralPath (Join-Path $projectDir 'System\PEData.plf'))) {
    Write-Warning "No System\PEData.plf beside the project file. If this is a project pointer (.ap16) rather than a project folder, pass the .ap16 inside the folder."
}

# ---------------------------------------------------------------- stage the assemblies

# Siemens.Engineering.dll resolves its dependencies from its own folder. The contract DLLs live in
# Bin, not next to it, so a plain LoadFrom of the V16 path fails to resolve them. Staging the
# Openness assemblies into one folder lets the CLR probe normally. Verified necessary.
if (-not (Test-Path -LiteralPath $StageDir)) { New-Item -ItemType Directory -Path $StageDir -Force | Out-Null }
Copy-Item (Join-Path $PublicApi '*.dll') $StageDir -Force
if (Test-Path -LiteralPath $TiaBin) {
    Copy-Item (Join-Path $TiaBin 'Siemens.Engineering*.dll') $StageDir -Force -ErrorAction SilentlyContinue
}

Write-Host "Loading Siemens.Engineering from staged folder..." -ForegroundColor Cyan
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $StageDir 'Siemens.Engineering.dll'))
Write-Host "  $($asm.FullName)" -ForegroundColor DarkGray

# Confirm by reflection that the API this script depends on really exists in this build,
# rather than trusting documentation or memory.
$required = @{
    'Siemens.Engineering.TiaPortal'                 = @('Projects', 'Dispose')
    'Siemens.Engineering.ProjectComposition'        = @('Open')
    'Siemens.Engineering.Project'                   = @('Devices', 'Name')
    'Siemens.Engineering.HW.Device'                 = @('DeviceItems', 'Name')
    'Siemens.Engineering.HW.DeviceItem'             = @('GetService', 'DeviceItems', 'TypeIdentifier')
    'Siemens.Engineering.HW.Features.SoftwareContainer' = @('Software')
    'Siemens.Engineering.SW.PlcSoftware'            = @('BlockGroup', 'TypeGroup', 'TagTableGroup')
    'Siemens.Engineering.SW.Blocks.PlcBlock'        = @('Name', 'ProgrammingLanguage', 'Number')
    'Siemens.Engineering.SW.Types.PlcType'          = @('Name')
    'Siemens.Engineering.SW.Tags.PlcTagTable'       = @('Name', 'Tags')
    'Siemens.Engineering.SW.Tags.PlcTag'            = @('Name', 'DataTypeName', 'LogicalAddress')
}
$missing = New-Object System.Collections.Generic.List[string]
foreach ($typeName in $required.Keys) {
    $type = $asm.GetType($typeName)
    if (-not $type) { $missing.Add("type $typeName"); continue }
    foreach ($member in $required[$typeName]) {
        if (-not ($type.GetMembers('Public,Instance') | Where-Object { $_.Name -eq $member })) {
            $missing.Add("$typeName.$member")
        }
    }
}
if ($missing.Count -gt 0) { throw "This V16 build does not expose: $($missing -join ', '). Update the script for this API." }
Write-Host "  API surface verified: $($required.Count) types, $($required.Values.Count) members." -ForegroundColor DarkGray

# ---------------------------------------------------------------- copy before opening

function Copy-TolerantTree([string]$source, [string]$destination) {
    # TIA holds the .ap16 open while a project is loaded, and Copy-Item fails on that lock.
    # Snapshot each file with FileShare.ReadWrite instead, so the project can be inspected
    # without the owner having to close it. Note this captures the on-disk state: edits sitting
    # unsaved in a live TIA session are not part of it.
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $unreadable = New-Object System.Collections.Generic.List[string]
    foreach ($file in (Get-ChildItem -LiteralPath $source -Recurse -File -Force)) {
        $relative = $file.FullName.Substring($source.Length).TrimStart('\')
        $target = Join-Path $destination $relative
        $targetDir = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }
        try {
            $in  = [System.IO.File]::Open($file.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            $out = [System.IO.File]::Open($target, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $in.CopyTo($out) } finally { $out.Dispose(); $in.Dispose() }
        }
        catch { $unreadable.Add("$relative :: $($_.Exception.Message)") }
    }
    return $unreadable
}

if ($AttachToOpen) {
    # Nothing to copy: TIA already holds this project open, and the .ap16 is locked exclusively.
    $copyAp16 = $null
    Write-Host "Attach mode: inspecting the loaded project in place, no copy needed." -ForegroundColor Cyan
}
else {
    New-Item -ItemType Directory -Path $CopyRoot -Force | Out-Null
    $copyDir = Join-Path $CopyRoot ([System.IO.Path]::GetFileName($projectDir))
    if (Test-Path -LiteralPath $copyDir) { Remove-Item -LiteralPath $copyDir -Recurse -Force }
    Write-Host "Copying project folder to a work copy..." -ForegroundColor Cyan
    $unreadable = Copy-TolerantTree $projectDir $copyDir
    if ($unreadable.Count -gt 0) {
        Write-Warning "$($unreadable.Count) file(s) could not be snapshotted; the work copy may be incomplete:"
        $unreadable | ForEach-Object { Write-Warning "  $_" }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $copyDir 'System\PEData.plf'))) {
        throw "The work copy has no System\PEData.plf, so it is not a usable project. Snapshot failed."
    }
    $copyAp16 = Get-ChildItem -LiteralPath $copyDir -Filter '*.ap16' | Select-Object -First 1
    if (-not $copyAp16) {
        throw ("No .ap16 file inside the copied folder '$copyDir'. TIA very likely holds the original " +
               "project open with an exclusive lock. Re-run with -AttachToOpen to inspect it in place.")
    }
    Write-Host "  work copy: $($copyAp16.FullName)" -ForegroundColor DarkGray
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$exportDir = Join-Path $OutputDir 'export'

# ---------------------------------------------------------------- helpers

$failures = New-Object System.Collections.Generic.List[object]
function Note-Failure([string]$where, [string]$message) {
    $script:failures.Add([pscustomobject]@{ Where = $where; Error = $message })
    Write-Host "  !! $where : $message" -ForegroundColor Yellow
}

function Try-Read([scriptblock]$action, [string]$where, $fallback = $null) {
    try { & $action } catch { Note-Failure $where $_.Exception.Message; return $fallback }
}

function Safe-Name([string]$n) { ($n -replace '[^\w\-. ]', '_') }

function As-Array($collection) {
    # PowerShell 5.1 throws "Argument types do not match" when assigning @($list) for a
    # List[object] that holds PSCustomObjects. List[string] is unaffected. Always materialise
    # with ToArray() rather than relying on the @() wrapper.
    if ($null -eq $collection) { return , @() }
    if ($collection -is [System.Array]) { return , $collection }
    return , $collection.ToArray()
}

function Export-OpennessFile([object]$owner, [string]$path, [string]$where) {
    # TIA refuses to overwrite an existing export target, so clear it first. Without this a
    # second run reports failures for files that already exist from the first.
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    Try-Read { $owner.Export((New-Object System.IO.FileInfo($path)), [Siemens.Engineering.ExportOptions]::None) } $where | Out-Null
}

function Get-CpuInfo([object]$deviceItem) {
    # Attribute support varies per device class, so probe a candidate list rather than assume.
    # "not supported by type" is an expected miss, not a fault, so it is not recorded as a failure.
    $info = [ordered]@{}
    foreach ($attribute in $CpuAttributes) {
        try {
            $value = $deviceItem.GetAttribute($attribute)
            if ($null -ne $value -and "$value" -ne '') { $info[$attribute] = "$value" }
        }
        catch {
            if ("$($_.Exception.Message)" -notmatch 'is not supported by type') {
                Note-Failure "CPU GetAttribute('$attribute')" $_.Exception.Message
            }
        }
    }
    return $info
}

# ---------------------------------------------------------------- collect

$inventory = [ordered]@{
    GeneratedUtc      = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    TiaAssembly       = $asm.FullName
    TiaRoot           = $TiaRoot
    OriginalProject   = $projectFile
    WorkCopy          = $copyAp16.FullName
    AttachedToRunning = ([bool]$UseRunningTia -or [bool]$AttachToOpen)
    Project           = $null
    Devices           = @()
    Failures          = @()
}

$tia = $null
$openedProject = $null
$openedByUs = $false
try {
    if ($AttachToOpen) {
        # GetProcesses() yields TiaPortalProcess, not TiaPortal; Attach() is what returns the
        # session object. Each TiaPortalProcess also reports the project it currently has open,
        # which is a more reliable match than the project name.
        Write-Host "Attaching to the running TIA Portal V16 that has this project open..." -ForegroundColor Cyan
        $processes = [Siemens.Engineering.TiaPortal]::GetProcesses()
        Write-Host "  $($processes.Count) TIA process(es) found:" -ForegroundColor DarkGray
        foreach ($candidate in $processes) {
            Write-Host "    pid $($candidate.Id) [$($candidate.Mode)] project=$($candidate.ProjectPath)" -ForegroundColor DarkGray
        }
        if ($processes.Count -eq 0) { throw "No TIA Portal process is running, so there is nothing to attach to." }

        $process = $processes | Where-Object { $_.ProjectPath -and $_.ProjectPath.FullName -eq $projectFile } | Select-Object -First 1
        if (-not $process) { $process = $processes | Where-Object { $_.ProjectPath } | Select-Object -First 1 }
        if (-not $process) { throw "No running TIA process has a project open." }

        $tia = $process.Attach()
        Write-Host "  attached to pid $($process.Id), project $($process.ProjectPath)" -ForegroundColor DarkGray

        $openProjects = @($tia.Projects)
        $wantedName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile)
        $openedProject = $openProjects | Where-Object { $_.Path -and $_.Path.FullName -eq $projectFile } | Select-Object -First 1
        if (-not $openedProject) { $openedProject = $openProjects | Where-Object { $_.Name -eq $wantedName } | Select-Object -First 1 }
        if (-not $openedProject -and $openProjects.Count -eq 1) { $openedProject = $openProjects[0] }
        if (-not $openedProject) {
            $names = ($openProjects | ForEach-Object { "$($_.Name) [$($_.Path)]" }) -join ', '
            throw "Could not identify which open project to inspect (looking for '$wantedName'). Open projects: $names"
        }
        Write-Host "  inspecting already-open project: $($openedProject.Name)" -ForegroundColor DarkGray
        Write-Host "  (read-only: this project will NOT be saved, modified or closed)" -ForegroundColor DarkGray
    }
    else {
        if ($UseRunningTia) {
            Write-Host "Attaching to a running TIA Portal V16..." -ForegroundColor Cyan
            $process = [Siemens.Engineering.TiaPortal]::GetProcesses() | Select-Object -First 1
            if (-not $process) { Write-Warning "No running TIA found; starting a headless instance instead." }
            else { $tia = $process.Attach() }
        }
        if (-not $tia) {
            Write-Host "Starting a headless TIA Portal V16 (WithoutUserInterface)..." -ForegroundColor Cyan
            $tia = [Siemens.Engineering.TiaPortal]::new([Siemens.Engineering.TiaPortalMode]::WithoutUserInterface)
        }

        Write-Host "Opening work copy..." -ForegroundColor Cyan
        $openedProject = $tia.Projects.Open((New-Object System.IO.FileInfo($copyAp16.FullName)))
        $openedByUs = $true
        Write-Host "  opened: $($openedProject.Name)" -ForegroundColor DarkGray
    }

    $inventory.Project = [ordered]@{
        Name         = $openedProject.Name
        Path         = Try-Read { $openedProject.Path.FullName } 'Project.Path'
        Version      = Try-Read { $openedProject.Version } 'Project.Version'
        Family       = Try-Read { $openedProject.Family } 'Project.Family'
        Author       = Try-Read { $openedProject.Author } 'Project.Author'
        CreationTime = Try-Read { "$($openedProject.CreationTime)" } 'Project.CreationTime'
        LastModified = Try-Read { "$($openedProject.LastModified)" } 'Project.LastModified'
        SizeBytes    = Try-Read { $openedProject.Size } 'Project.Size' -1
        IsModified   = Try-Read { $openedProject.IsModified } 'Project.IsModified'
        WasAlreadyOpen = (-not $openedByUs)
    }

    foreach ($device in $openedProject.Devices) {
        Write-Host "Device: $($device.Name)" -ForegroundColor Cyan
        $deviceEntry = [ordered]@{
            Name           = $device.Name
            TypeIdentifier = Try-Read { $device.TypeIdentifier } "Device.TypeIdentifier"
            Cpu            = $null
            Blocks         = @()
            Udts           = @()
            TagTables      = @()
        }

        # Find the PLC software: a device item that is a software container holding PlcSoftware.
        $plc = $null
        $cpuItem = $null
        $getSoftwareContainer = [Siemens.Engineering.HW.DeviceItem].GetMethod('GetService').
            MakeGenericMethod([Siemens.Engineering.HW.Features.SoftwareContainer])
        foreach ($item in $device.DeviceItems) {
            $found = Try-Read {
                # PowerShell 5.1 cannot call a generic method with $x.Method[T]() syntax
                # (that arrived in PS 7), so close the generic over the software container type.
                $container = $getSoftwareContainer.Invoke($item, @())
                if ($container -and $container.Software) {
                    foreach ($candidate in @($container.Software)) {
                        if ($candidate -is [Siemens.Engineering.SW.PlcSoftware]) { return $candidate }
                    }
                }
                return $null
            } "GetService<SoftwareContainer> on '$($item.Name)'"
            if ($found) { $plc = $found; $cpuItem = $item; break }
        }

        if (-not $plc) {
            Note-Failure "Device '$($device.Name)'" 'No PlcSoftware found under this device (no software container held a PlcSoftware).'
            $inventory.Devices += $deviceEntry
            continue
        }

        $deviceEntry.Cpu = [ordered]@{
            DeviceItemName = $cpuItem.Name
            TypeIdentifier = Try-Read { $cpuItem.TypeIdentifier } 'DeviceItem.TypeIdentifier'
            PositionNumber = Try-Read { $cpuItem.PositionNumber } 'DeviceItem.PositionNumber'
            Attributes      = Get-CpuInfo $cpuItem
            PlcSoftwareName = $plc.Name
        }
        Write-Host "  CPU: $($cpuItem.Name)  ($($deviceEntry.Cpu.TypeIdentifier))" -ForegroundColor DarkGray

        # --- blocks (recursive into user groups)
        Write-Host "  reading blocks..." -ForegroundColor DarkGray
        $blockList = New-Object System.Collections.Generic.List[object]
        function Read-Blocks([object]$group, [string]$path) {
            foreach ($block in $group.Blocks) {
                # V16 names these FB / FC / OB / DataBlock with no "Plc" prefix. Order matters:
                # InstanceDB, GlobalDB and ArrayDB all derive from DataBlock.
                $kind = 'Block'
                if ($block -is [Siemens.Engineering.SW.Blocks.ArrayDB]) { $kind = 'ArrayDB' }
                elseif ($block -is [Siemens.Engineering.SW.Blocks.GlobalDB]) { $kind = 'GlobalDB' }
                elseif ($block -is [Siemens.Engineering.SW.Blocks.InstanceDB]) { $kind = 'InstanceDB' }
                elseif ($block -is [Siemens.Engineering.SW.Blocks.DataBlock]) { $kind = 'DB' }
                elseif ($block -is [Siemens.Engineering.SW.Blocks.OB]) { $kind = 'OB' }
                elseif ($block -is [Siemens.Engineering.SW.Blocks.FB]) { $kind = 'FB' }
                elseif ($block -is [Siemens.Engineering.SW.Blocks.FC]) { $kind = 'FC' }
                elseif ($block -is [Siemens.Engineering.SW.Blocks.CodeBlock]) { $kind = 'CodeBlock' }

                $script:blockList.Add([pscustomobject]@{
                    Name    = $block.Name
                    Number  = Try-Read { $block.Number } "block $($block.Name).Number" -1
                    Kind    = $kind
                    Language= "$($block.ProgrammingLanguage)"
                    Group   = $path
                })

                if ($Export) {
                    $target = Join-Path (Join-Path $exportDir 'blocks') (Safe-Name $block.Name)
                    $targetDir = Split-Path -Parent $target
                    if (-not (Test-Path -LiteralPath $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }
                    $leaf = "$($targetDir)\$($block.Name).xml"
                    Export-OpennessFile $block $leaf "Export block '$($block.Name)'"
                }
            }
            foreach ($sub in $group.Groups) { Read-Blocks $sub "$path/$($sub.Name)" }
        }
        Read-Blocks $plc.BlockGroup ''
        $deviceEntry.Blocks = As-Array $blockList
        Write-Host "    $($blockList.Count) block(s)" -ForegroundColor DarkGray

        # --- UDTs
        # V16 PlcType exposes no Members property and no PlcTypeMember type exists, so the only
        # way to see inside a UDT is to export it and read the SimaticML. Export to a scratch
        # folder always (members are part of the inventory), and additionally into exportDir when
        # -Export was asked for.
        Write-Host "  reading UDTs..." -ForegroundColor DarkGray
        $udtScratch = Join-Path $CopyRoot 'udt-scratch'
        New-Item -ItemType Directory -Path $udtScratch -Force | Out-Null
        $typeList = New-Object System.Collections.Generic.List[object]
        function Read-Types([object]$group, [string]$path) {
            foreach ($type in $group.Types) {
                $members = New-Object System.Collections.Generic.List[string]
                $scratchFile = Join-Path $udtScratch "$(Safe-Name $type.Name).xml"
                # TIA refuses to overwrite an existing export target.
                Remove-Item -LiteralPath $scratchFile -Force -ErrorAction SilentlyContinue
                $exported = Try-Read {
                    $type.Export((New-Object System.IO.FileInfo($scratchFile)), [Siemens.Engineering.ExportOptions]::None)
                    return $true
                } "Export UDT '$($type.Name)' for member inspection" $false

                if ($exported -and (Test-Path -LiteralPath $scratchFile)) {
                    $xml = Try-Read { [xml](Get-Content -LiteralPath $scratchFile -Raw) } "Parse UDT XML '$($type.Name)'"
                    if ($xml -and $xml.DocumentElement) {
                        foreach ($m in $xml.SelectNodes('//Interface/Member')) {
                            $n = $m.GetAttribute('Name'); $ty = $m.GetAttribute('Type')
                            $opt = $m.GetAttribute('OptimizedAccess')
                            $members.Add(("{0} : {1}{2}" -f $n, $ty, $(if ($opt) { " ($opt)" } else { '' })))
                        }
                    }
                    if ($Export) {
                        $targetDir = Join-Path $exportDir 'udts'
                        if (-not (Test-Path -LiteralPath $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }
                        $leaf = Join-Path $targetDir "$(Safe-Name $type.Name).xml"
                        Try-Read { Copy-Item -LiteralPath $scratchFile -Destination $leaf -Force } "Save UDT '$($type.Name)'" | Out-Null
                    }
                }

                $script:typeList.Add([pscustomobject]@{
                    Name        = $type.Name
                    Group       = $path
                    MemberCount = $members.Count
                    Members     = As-Array $members
                })
            }
            foreach ($sub in $group.Groups) { Read-Types $sub "$path/$($sub.Name)" }
        }
        Read-Types $plc.TypeGroup ''
        $deviceEntry.Udts = As-Array $typeList
        Write-Host "    $($typeList.Count) UDT(s)" -ForegroundColor DarkGray

        # --- tag tables
        Write-Host "  reading tag tables..." -ForegroundColor DarkGray
        $tableList = New-Object System.Collections.Generic.List[object]
        function Read-Tables([object]$group, [string]$path) {
            foreach ($table in $group.TagTables) {
                $tags = New-Object System.Collections.Generic.List[object]
                foreach ($tag in (Try-Read { $table.Tags } "TagTable '$($table.Name)'.Tags" @())) {
                    $tags.Add([pscustomobject]@{
                        Name          = $tag.Name
                        DataTypeName  = $tag.DataTypeName
                        LogicalAddress= $tag.LogicalAddress
                    })
                }
                $script:tableList.Add([pscustomobject]@{
                    Name  = $table.Name
                    Group = $path
                    Count = $tags.Count
                    Tags  = As-Array $tags
                })
                if ($Export) {
                    $targetDir = Join-Path $exportDir 'tagtables'
                    if (-not (Test-Path -LiteralPath $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }
                    $leaf = Join-Path $targetDir "$($table.Name).xml"
                    Export-OpennessFile $table $leaf "Export tag table '$($table.Name)'"
                }
            }
            foreach ($sub in $group.Groups) { Read-Tables $sub "$path/$($sub.Name)" }
        }
        Read-Tables $plc.TagTableGroup ''
        $deviceEntry.TagTables = As-Array $tableList
        Write-Host "    $($tableList.Count) tag table(s), $(($tableList | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum) tag(s) total" -ForegroundColor DarkGray

        $inventory.Devices += $deviceEntry
    }
}
catch {
    Note-Failure 'Openness' "$($_.Exception.GetType().FullName): $($_.Exception.Message)"
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "If this is an access error, the Windows user running this script must be a member of 'Siemens TIA Openness' and must have signed out and back in since that change." -ForegroundColor Yellow
}
finally {
    # Never close or save a project that was already open in the owner's session.
    if ($openedProject -and $openedByUs) { Try-Read { $openedProject.Close() } 'Project.Close' | Out-Null }
    if ($tia -and $openedByUs) { Try-Read { $tia.Dispose() } 'TiaPortal.Dispose' | Out-Null }
}

$inventory.Failures = As-Array $failures

# ---------------------------------------------------------------- summarise

$allBlocks   = @($inventory.Devices | ForEach-Object { $_.Blocks })
$allTypes    = @($inventory.Devices | ForEach-Object { $_.Udts })
$allTables   = @($inventory.Devices | ForEach-Object { $_.TagTables })
$allTags     = @($allTables | ForEach-Object { $_.Tags })

$byLanguage = $allBlocks | Group-Object Language | Sort-Object Name
$byKind     = $allBlocks | Group-Object Kind | Sort-Object Name

$has = @{
    'SCL block'                    = [bool]($allBlocks | Where-Object { $_.Language -eq 'SCL' })
    'FBD block'                    = [bool]($allBlocks | Where-Object { $_.Language -eq 'FBD' })
    'LAD block'                    = [bool]($allBlocks | Where-Object { $_.Language -eq 'LAD' })
    'STL block'                    = [bool]($allBlocks | Where-Object { $_.Language -eq 'STL' })
    'GRAPH block'                  = [bool]($allBlocks | Where-Object { $_.Language -eq 'GRAPH' })
    'UDT'                          = ($allTypes.Count -gt 0)
    'tag table'                    = ($allTables.Count -gt 0)
    'tag with %I address'          = [bool]($allTags | Where-Object { $_.LogicalAddress -like '%I*' })
    'tag with %Q address'          = [bool]($allTags | Where-Object { $_.LogicalAddress -like '%Q*' })
    'tag with %M address'          = [bool]($allTags | Where-Object { $_.LogicalAddress -like '%M*' })
    'global DB'                    = [bool]($allBlocks | Where-Object { $_.Kind -in @('GlobalDB','DB') })
    'instance DB'                  = [bool]($allBlocks | Where-Object { $_.Kind -eq 'InstanceDB' })
    'FB'                           = [bool]($allBlocks | Where-Object { $_.Kind -eq 'FB' })
    'FC'                           = [bool]($allBlocks | Where-Object { $_.Kind -eq 'FC' })
    'OB'                           = [bool]($allBlocks | Where-Object { $_.Kind -eq 'OB' })
    'block group (nested folder)'  = [bool]($allBlocks | Where-Object { $_.Group })
}

# ---------------------------------------------------------------- write JSON

$jsonPath = Join-Path $OutputDir 'inventory.json'
$inventory | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-Host "Wrote $jsonPath" -ForegroundColor Green

# ---------------------------------------------------------------- write Markdown

$md = New-Object System.Text.StringBuilder
function Line([string]$s = '') { [void]$md.AppendLine($s) }

Line '# TIA Portal V16 project inventory'
Line ''
Line ("Generated " + $inventory.GeneratedUtc + " by tools\Inspect-TiaProject.ps1.")
Line ''
Line "**TIA assembly:** ``$($asm.FullName)``"
Line ''
Line "**Original project (never opened):** ``$($inventory.OriginalProject)``"
if ($inventory.AttachedToRunning) {
    Line '**How it was read:** attached to the TIA session that already had this project open (read-only).'
}
elseif ($inventory.WorkCopy) {
    Line "**Work copy that was actually opened:** ``$($inventory.WorkCopy)``"
}
else {
    Line '**Work copy that was actually opened:** (none - the project could not be opened)'
}
Line ''
Line '## Summary'
Line ''
Line "| Item | Count |"
Line '|---|---|'
Line "| Devices | $($inventory.Devices.Count) |"
Line "| Blocks | $($allBlocks.Count) |"
Line "| UDTs | $($allTypes.Count) |"
Line "| Tag tables | $($allTables.Count) |"
Line "| Tags | $($allTags.Count) |"
Line "| Read failures | $($failures.Count) |"
Line ''
Line '## Does this project contain'
Line ''
Line '| Feature | Present |'
Line '|---|---|'
foreach ($key in ($has.Keys | Sort-Object)) { Line "| $key | $(if ($has[$key]) { 'yes' } else { 'NO' }) |" }
Line ''
Line '## Blocks by language'
Line ''
Line '| Language | Count |'
Line '|---|---|'
foreach ($g in $byLanguage) { Line "| $($g.Name) | $($g.Count) |" }
Line ''
Line '## Blocks by kind'
Line ''
Line '| Kind | Count |'
Line '|---|---|'
foreach ($g in $byKind) { Line "| $($g.Name) | $($g.Count) |" }

foreach ($device in $inventory.Devices) {
    Line ''
    Line "## Device: $($device.Name)"
    Line ''
    Line "- TypeIdentifier: ``$($device.TypeIdentifier)``"
    if ($device.Cpu) {
        Line "- CPU device item: ``$($device.Cpu.DeviceItemName)``"
        Line "- CPU TypeIdentifier: ``$($device.Cpu.TypeIdentifier)``"
        Line "- PositionNumber: ``$($device.Cpu.PositionNumber)``"
        Line "- PlcSoftware: ``$($device.Cpu.PlcSoftwareName)``"
        foreach ($key in $device.Cpu.Attributes.Keys) { Line "- $($key): ``$($device.Cpu.Attributes[$key])``" }
    }

    if (@($device.Blocks).Count -gt 0) {
        Line ''
        Line '### Blocks'
        Line ''
        Line '| Name | Number | Kind | Language | Group |'
        Line '|---|---|---|---|---|'
        foreach ($b in $device.Blocks) { Line "| $($b.Name) | $($b.Number) | $($b.Kind) | $($b.Language) | $($b.Group) |" }
    }

    if (@($device.Udts).Count -gt 0) {
        Line ''
        Line '### UDTs'
        Line ''
        foreach ($t in $device.Udts) {
            Line "**$($t.Name)** ($(@($t.Members).Count) member(s))"
            Line ''
            if (@($t.Members).Count -gt 0) { Line '```'; foreach ($m in $t.Members) { Line $m }; Line '```'; Line '' }
        }
    }

    if (@($device.TagTables).Count -gt 0) {
        Line ''
        Line '### Tag tables'
        Line ''
        foreach ($t in $device.TagTables) {
            Line "**$($t.Name)** - $($t.Count) tag(s)"
            Line ''
            if ($t.Count -gt 0) {
                Line '| Name | Type | Logical address |'
                Line '|---|---|---|'
                foreach ($tag in $t.Tags) { Line "| $($tag.Name) | $($tag.DataTypeName) | $($tag.LogicalAddress) |" }
                Line ''
            }
        }
    }
}

Line ''
Line '## What could not be read'
Line ''
if ($failures.Count -eq 0) {
    Line 'Nothing failed. Every device, block, UDT and tag table enumerated cleanly.'
}
else {
    Line '| Where | Error |'
    Line '|---|---|'
    foreach ($f in $failures) {
        # Collapse to one line: a multi-line message would otherwise break the table.
        $flat = ($f.Error -replace '\s+', ' ').Trim()
        Line "| $($f.Where) | $($flat -replace '\|', '\|') |"
    }
}

Line ''
Line '## Export status'
Line ''
if ($Export) {
    Line "Objects were exported to ``$exportDir``."
    Line ''
    Line 'These files are the authoritative format reference. Do not hand-edit them and do not assume'
    Line 'any generator output is correct until it is diffed against them.'
}
else {
    Line 'No export was requested. Re-run with -Export to write the goldens.'
}

$mdPath = Join-Path $OutputDir 'inventory.md'
$md.ToString() | Set-Content -LiteralPath $mdPath -Encoding UTF8
Write-Host "Wrote $mdPath" -ForegroundColor Green

Write-Host ''
Write-Host "blocks=$($allBlocks.Count) udts=$($allTypes.Count) tagTables=$($allTables.Count) tags=$($allTags.Count) failures=$($failures.Count)" -ForegroundColor Cyan
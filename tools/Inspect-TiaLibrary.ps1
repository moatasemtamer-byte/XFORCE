<#
.SYNOPSIS
    Inventories and exports a TIA Portal V16 global library through the Openness API.

.DESCRIPTION
    A sample project is not a reliable source of format ground truth: the V16 sample in the
    repository turned out to be an empty project shell. Real Siemens global libraries shipped with
    TIA V16 do contain blocks and UDTs, and they are the correct reference for what TIA V16
    actually accepts.

    For each library type this reports the name, kind (code block / UDT), version number and
    state, and with -Export writes every version's source to disk.

    Libraries are snapshotted to a scratch folder and opened with OpenMode.ReadOnly, so the
    original library is never modified.

.EXAMPLE
    .\Inspect-TiaLibrary.ps1 -LibraryPath '...\lib\TIA 16\LGF\LGF.al16' -OutputDir 'C:\XforceMsw\goldens\v16\lib\LGF' -Export
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LibraryPath,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [switch]$Export
)

$ErrorActionPreference = 'Stop'

$TiaRoot   = 'C:\Program Files\Siemens\Automation\Portal V16'
$PublicApi = Join-Path $TiaRoot 'PublicAPI\V16'
$TiaBin    = Join-Path $TiaRoot 'Bin'
$Dll       = Join-Path $PublicApi 'Siemens.Engineering.dll'
$StageDir  = Join-Path $env:TEMP ("tiaportal-v16-probe-" + [System.Guid]::NewGuid().ToString('N').Substring(0,8))
$WorkDir   = Join-Path $env:TEMP 'tiaportal-v16-libwork'

if (-not (Test-Path -LiteralPath $Dll)) { throw "TIA V16 Openness DLL not found at '$Dll'." }
if (-not (Test-Path -LiteralPath $LibraryPath)) { throw "Library entry file not found: '$LibraryPath'." }

$failures = New-Object System.Collections.Generic.List[object]
function Note-Failure([string]$where, [string]$message) {
    $script:failures.Add([pscustomobject]@{ Where = $where; Error = $message })
    Write-Host "  !! $where :: $message" -ForegroundColor Yellow
}
function As-Array($collection) {
    if ($null -eq $collection) { return , @() }
    if ($collection -is [System.Array]) { return , $collection }
    return , $collection.ToArray()
}
function Safe-Name([string]$n) { ($n -replace '[^\w\-. ]', '_') }

# ---------------------------------------------------------------- stage assemblies

New-Item -ItemType Directory -Path $StageDir -Force | Out-Null
# A unique staging folder per run, so a concurrent or lingering run cannot hold a lock on the
# Siemens.Engineering.dll we are trying to copy.
Copy-Item (Join-Path $PublicApi '*.dll') $StageDir -Force
if (Test-Path -LiteralPath $TiaBin) {
    Copy-Item (Join-Path $TiaBin 'Siemens.Engineering*.dll') $StageDir -Force -ErrorAction SilentlyContinue
}
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $StageDir 'Siemens.Engineering.dll'))

$required = @{
    'Siemens.Engineering.TiaPortal'                    = @('GlobalLibraries')
    'Siemens.Engineering.Library.GlobalLibraryComposition' = @('Open')
    'Siemens.Engineering.Library.GlobalLibrary'        = @('TypeFolder', 'Name')
    'Siemens.Engineering.Library.Types.LibraryTypeSystemFolder' = @('Types', 'Folders')
    'Siemens.Engineering.Library.Types.LibraryType'    = @('Name', 'Versions')
}
$missing = New-Object System.Collections.Generic.List[string]
foreach ($typeName in $required.Keys) {
    $type = $asm.GetType($typeName)
    if (-not $type) { $missing.Add("type $typeName"); continue }
    foreach ($member in $required[$typeName]) {
        if (-not ($type.GetMembers('Public,Instance') | Where-Object { $_.Name -eq $member })) { $missing.Add("$typeName.$member") }
    }
}
if ($missing.Count -gt 0) { throw "This V16 build does not expose: $($missing -join ', ')." }

# ---------------------------------------------------------------- snapshot the library

$entry = (Resolve-Path -LiteralPath $LibraryPath).Path
$libDir = Split-Path -Parent $entry
New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
$snapshot = Join-Path $WorkDir (Split-Path -Leaf $libDir)
if (Test-Path -LiteralPath $snapshot) { Remove-Item -LiteralPath $snapshot -Recurse -Force }
Write-Host "Snapshotting library to $snapshot ..." -ForegroundColor Cyan
Copy-Item -LiteralPath $libDir -Destination $snapshot -Recurse -Force
Get-ChildItem -LiteralPath $snapshot -Recurse -Force -EA SilentlyContinue |
    Where-Object { $_.Name -match '\.lock$' } | Remove-Item -Force -EA SilentlyContinue
$snapshotEntry = Get-ChildItem -LiteralPath $snapshot -Filter '*.al16' | Select-Object -First 1
if (-not $snapshotEntry) { throw "No .al16 entry file in the snapshot of '$libDir'." }

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# ---------------------------------------------------------------- collect

$inventory = [ordered]@{
    GeneratedUtc   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    TiaAssembly    = $asm.FullName
    OriginalLibrary= $entry
    Snapshot       = $snapshotEntry.FullName
    OpenMode       = 'ReadOnly'
    Name           = $null
    Comment        = $null
    Author         = $null
    Types          = @()
    Failures       = @()
}

$tia = $null
$library = $null
try {
    Write-Host "Starting a headless TIA Portal V16..." -ForegroundColor Cyan
    $tia = [Siemens.Engineering.TiaPortal]::new([Siemens.Engineering.TiaPortalMode]::WithoutUserInterface)

    Write-Host "Opening library read-only..." -ForegroundColor Cyan
    $library = $tia.GlobalLibraries.Open(
        (New-Object System.IO.FileInfo($snapshotEntry.FullName)),
        [Siemens.Engineering.OpenMode]::ReadOnly)

    $inventory.Name = $library.Name
    $inventory.Author = $library.Author
    try { $inventory.Comment = "$($library.Comment)" } catch { }
    Write-Host "  library: $($library.Name)" -ForegroundColor DarkGray

    $typeList = New-Object System.Collections.Generic.List[object]
    $script:typeCounter = 0

    # Library types are nested in folders: the root "Types" folder can be empty while everything
    # lives under a folder named after the library. Recurse the whole tree.
    function Read-LibraryTypes([object]$folder, [string]$path) {
        foreach ($type in $folder.Types) {
            $script:typeCounter++
            if ($script:typeCounter % 100 -eq 0) { Write-Host "    ...$script:typeCounter types" -ForegroundColor DarkGray }

            $kind = 'Other'
            if ($type -is [Siemens.Engineering.SW.Blocks.CodeBlockLibraryType]) { $kind = 'CodeBlock' }
            elseif ($type -is [Siemens.Engineering.SW.Types.PlcTypeLibraryType]) { $kind = 'PlcType' }

            $versions = New-Object System.Collections.Generic.List[object]
            foreach ($version in $type.Versions) {
                $versionEntry = [ordered]@{
                    VersionNumber = "$($version.VersionNumber)"
                    State         = "$($version.State)"
                    Author        = $version.Author
                    ModifiedDate  = "$($version.ModifiedDate)"
                    ExportedTo    = $null
                }

                if ($Export) {
                    # The folder path must be part of the filename: two types in different
                    # folders can share a name, and a flat name silently overwrites one
                    # of them (LBC lost 96 of 272 that way).
                    $flat = "{0}__{1}" -f (Safe-Name $type.Name), (Safe-Name "$($version.VersionNumber)")
                    if ($path) { $flat = (Safe-Name $path.Trim('/')) + '__' + $flat }
                    $leaf = Join-Path $OutputDir "$flat.xml"
                    Remove-Item -LiteralPath $leaf -Force -ErrorAction SilentlyContinue
                    try {
                        $version.Export((New-Object System.IO.FileInfo($leaf)), [Siemens.Engineering.ExportOptions]::None)
                        $versionEntry.ExportedTo = $leaf
                    }
                    catch { Note-Failure "Export '$($type.Name)' $($version.VersionNumber)" $_.Exception.Message }
                }
                $versions.Add([pscustomobject]$versionEntry)
            }

            $typeList.Add([pscustomobject]@{
                Name     = $type.Name
                Kind     = $kind
                Folder   = $path
                Guid     = "$(try { $type.Guid } catch { '' })"
                Versions = As-Array $versions
            })
        }
        foreach ($sub in $folder.Folders) { Read-LibraryTypes $sub "$path/$($sub.Name)" }
    }
    Read-LibraryTypes $library.TypeFolder ''
    $inventory.Types = As-Array $typeList
    Write-Host "  $($typeList.Count) library type(s)" -ForegroundColor DarkGray
}
catch {
    Note-Failure 'Openness' "$($_.Exception.GetType().FullName): $($_.Exception.Message)"
    Write-Host $_.Exception.Message -ForegroundColor Red
}
finally {
    if ($library) { try { $library.Close() } catch { } }
    if ($tia) { try { $tia.Dispose() } catch { } }
}

$inventory.Failures = As-Array $failures

# ---------------------------------------------------------------- write reports

$jsonPath = Join-Path $OutputDir 'library-inventory.json'
$inventory | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-Host "Wrote $jsonPath" -ForegroundColor Green

$types = @($inventory.Types)
$blocks = @($types | Where-Object { $_.Kind -eq 'CodeBlock' })
$plcTypes = @($types | Where-Object { $_.Kind -eq 'PlcType' })
$exported = @($types | ForEach-Object { $_.Versions } | Where-Object { $_.ExportedTo })

$md = New-Object System.Text.StringBuilder
function Line([string]$s = '') { [void]$md.AppendLine($s) }

Line "# Library inventory: $($inventory.Name)"
Line ''
Line "Generated $($inventory.GeneratedUtc) by `tools\Inspect-TiaLibrary.ps1`."
Line ''
Line "**TIA assembly:** ``$($asm.FullName)``"
Line "**Original library (never opened):** ``$($inventory.OriginalLibrary)``"
Line "**Opened as:** snapshot, ``OpenMode=$($inventory.OpenMode)``"
Line ''
Line '## Summary'
Line ''
Line '| Item | Count |'
Line '|---|---|'
Line "| Library types | $($types.Count) |"
Line "| Code block types | $($blocks.Count) |"
Line "| PLC/UDT types | $($plcTypes.Count) |"
Line "| Versions exported | $($exported.Count) |"
Line "| Read/export failures | $($failures.Count) |"
Line ''
Line '## All types'
Line ''
Line '| Name | Kind | Versions |'
Line '|---|---|---|'
foreach ($t in $types) {
    $vs = (@($t.Versions) | ForEach-Object { "$($_.VersionNumber)$(if ($_.ExportedTo) { ' [exported]' } else { '' })" }) -join ', '
    Line "| $($t.Name) | $($t.Kind) | $vs |"
}
Line ''
Line '## What could not be read'
Line ''
if ($failures.Count -eq 0) { Line 'Nothing failed.' }
else {
    Line '| Where | Error |'
    Line '|---|---|'
    foreach ($f in $failures) { Line "| $($f.Where) | $((($f.Error -replace '\s+',' ').Trim()) -replace '\|','\|') |" }
}

$mdPath = Join-Path $OutputDir 'library-inventory.md'
$md.ToString() | Set-Content -LiteralPath $mdPath -Encoding UTF8
Write-Host "Wrote $mdPath" -ForegroundColor Green
Write-Host "types=$($types.Count) blocks=$($blocks.Count) plcTypes=$($plcTypes.Count) exported=$($exported.Count) failures=$($failures.Count)" -ForegroundColor Cyan
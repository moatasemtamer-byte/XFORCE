# Compares a generated PT2X against a reference TriStation project.
# Usage: powershell -File scripts/compare-pt2x.ps1 -Reference <ref.pt2x> -Generated <ours.pt2x>
param(
    [Parameter(Mandatory = $true)][string]$Reference,
    [Parameter(Mandatory = $true)][string]$Generated
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'pt2-conv\src\Pt2Conv\bin\Release\net7.0\pt2conv.exe'
if (-not (Test-Path $exe)) {
    throw "pt2conv not built. Run: dotnet build pt2-conv/src/Pt2Conv/Pt2Conv.csproj -c Release"
}

Write-Host "== verify (ours) =="
& $exe verify $Generated

Write-Host ""
Write-Host "== stream diff (ref -> ours) =="
& $exe diff $Reference $Generated

Write-Host ""
Write-Host "Tip: extract both containers and compare the FBD streams under Current/Elements/*."

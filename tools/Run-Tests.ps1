#Requires -Version 5.1
<#
.SYNOPSIS
  Runs the test solution and writes a single merged status\tests.trx evidence file.

.DESCRIPTION
  `dotnet test` on a solution writes one TRX per test project. When a fixed
  LogFileName is used the projects overwrite each other, so the on-disk file only
  ever contains the last project. This script runs each test project into its own
  uniquely named TRX, then merges the counters into a single valid TRX at
  status\tests.trx, which is the file the GUI status engine reads.
#>
[CmdletBinding()]
param(
  [string]$Solution,
  [string]$StatusDir
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $Solution)  { $Solution  = Join-Path $repoRoot 'engine\TriconexToTia.sln' }
if (-not $StatusDir) { $StatusDir = Join-Path $repoRoot 'status' }

if (-not (Test-Path -LiteralPath $Solution)) { throw "Solution not found: $Solution" }
New-Item -ItemType Directory -Force -Path $StatusDir | Out-Null
Get-ChildItem -LiteralPath $StatusDir -Filter '*.trx' -ErrorAction SilentlyContinue | Remove-Item -Force

$projects = Get-ChildItem -Path (Join-Path $repoRoot 'engine\tests') -Recurse -Filter '*.csproj' |
  Sort-Object FullName

Write-Host "Found $($projects.Count) test project(s)"

$failed = 0
foreach ($project in $projects) {
  $name = [IO.Path]::GetFileNameWithoutExtension($project.Name)
  $trxName = "$name.trx"
  Write-Host "== $name =="
  & dotnet test $project.FullName --nologo --logger "trx;LogFileName=$trxName" --results-directory $StatusDir
  if ($LASTEXITCODE -ne 0) { $failed++ }
}

$total = 0; $executed = 0; $passed = 0; $skipped = 0; $testFailed = 0
$individual = Get-ChildItem -LiteralPath $StatusDir -Filter '*.trx'
foreach ($file in $individual) {
  [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
  $counters = $xml.TestRun.ResultSummary.Counters
  if ($null -eq $counters) { continue }
  $total     += [int]$counters.total
  $executed  += [int]$counters.executed
  $passed    += [int]$counters.passed
  $testFailed += [int]$counters.failed
  $skipped   += [int]$counters.skipped
}

$outcome = if ($testFailed -gt 0) { 'Failed' } else { 'Passed' }
$runId = [Guid]::NewGuid().ToString()
$now = [DateTime]::UtcNow.ToString('o')
$trx = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="$runId" name="Merged run" runUser="$env:USERNAME" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Times creation="$now" queuing="$now" start="$now" finish="$now" />
  <ResultSummary outcome="$outcome">
    <Counters total="$total" executed="$executed" passed="$passed" failed="$testFailed" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
  </ResultSummary>
</TestRun>
"@
$mergedPath = Join-Path $StatusDir 'tests.trx'
Set-Content -LiteralPath $mergedPath -Value $trx -Encoding UTF8

# Keep only the merged evidence file.
$individual | Remove-Item -Force

Write-Host ("Merged: total={0} passed={1} failed={2} skipped={3} outcome={4}" -f $total, $passed, $testFailed, $skipped, $outcome)
Write-Host "Written: $mergedPath"
if ($failed -gt 0 -or $testFailed -gt 0) { exit 1 }
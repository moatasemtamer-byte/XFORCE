param(
    [int]$CheckSeconds = 5,
    [int]$HealthTimeoutSeconds = 30
)

$dll  = "C:\XforceMsw\engine\src\TriconexToTia.Server\bin\Release\net7.0\TriconexToTia.Server.dll"
$cr   = "C:\Users\Moatasem\circuit-reader\csharp\src\CircuitReader.Cli\bin\Release\net7.0\circuit-reader.exe"
$cli  = "C:\XforceMsw\tools\BlLadderCli\bin\Release\net48\BlLadderCli.exe"
$log  = "C:\Users\Moatasem\AppData\Local\Temp\opencode\blrun\engine_srv.log"
$prev = "C:\Users\Moatasem\AppData\Local\Temp\opencode\blrun\engine_srv.prev.log"

function Log([string]$msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg
    Add-Content -LiteralPath $log -Value $line -Encoding UTF8
}

function Test-EngineHealth {
    try {
        $r = Invoke-RestMethod "http://localhost:5080/health" -TimeoutSec 3
        return [bool]$r
    } catch { return $false }
}

function Get-EngineProcs {
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*TriconexToTia*' }
}

function Stop-EngineProcs {
    foreach ($p in Get-EngineProcs) {
        try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop; Log "stopped stray engine pid=$($p.ProcessId)" } catch {}
    }
}

function Start-Engine {
    $cmd = 'cmd /c set PORT=5080&& set MSW_CIRCUITREADER_PATH=' + $cr +
           '&& set MSW_LADDERCLI_PATH=' + $cli +
           '&& dotnet "' + $dll + '" >> "' + $log + '" 2>&1'
    $r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=$cmd}
    if ($r.ReturnValue -ne 0) { Log "spawn failed rv=$($r.ReturnValue)"; return }
    Log "spawned engine attempt (pid=$($r.ProcessId))"
    $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if (Test-EngineHealth) { Log "engine healthy"; return }
    }
    Log "engine did not become healthy within ${HealthTimeoutSeconds}s"
    Stop-EngineProcs
}

if (Test-Path $log) { Move-Item -LiteralPath $log -Destination $prev -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path (Split-Path $log) -Force | Out-Null
Log "supervisor started pid=$PID check=${CheckSeconds}s"

$failures = 0
while ($true) {
    if (Test-EngineHealth) {
        $failures = 0
        Start-Sleep -Seconds $CheckSeconds
        continue
    }
    Log "engine DOWN - restarting"
    Stop-EngineProcs
    Start-Sleep -Seconds 1
    Start-Engine
    if (Test-EngineHealth) {
        $failures = 0
    } else {
        $failures++
        $backoff = [Math]::Min(30, 5 * $failures)
        Log "restart failed ($failures) - backing off ${backoff}s"
        Start-Sleep -Seconds $backoff
    }
}

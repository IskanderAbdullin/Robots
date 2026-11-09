[CmdletBinding()]
param(
    [string]$Module = 'all',
    [int]$Seed = 4217,
    [ValidateRange(1, 1000000)][int]$Iterations = 1000,
    [string]$RobotSource = '',
    [string]$EngineBin = '',
    [switch]$Baseline,
    [switch]$SkipRealApi
)

$ErrorActionPreference = 'Stop'
$standRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RobotSource)) {
    if ($Baseline) {
        $RobotSource = Join-Path $standRoot 'Baseline\MarketMakerStakan3.cs.txt'
    } elseif (Test-Path -LiteralPath (Join-Path $standRoot '..\MarketMakerStakan3.cs')) {
        $RobotSource = Join-Path $standRoot '..\MarketMakerStakan3.cs'
    } else {
        $RobotSource = Join-Path $standRoot '..\..\Robots\MarketMakerStakan3.cs'
    }
}
$RobotSource = (Resolve-Path -LiteralPath $RobotSource).Path
if ([string]::IsNullOrWhiteSpace($EngineBin)) {
    $localEngineBin = [IO.Path]::GetFullPath((Join-Path $standRoot '..\..\..'))
    if (Test-Path -LiteralPath (Join-Path $localEngineBin 'OsEngine.dll')) {
        $EngineBin = $localEngineBin
    } else {
        $EngineBin = 'R:\1.OSENGINE\02.11.2026 для написания\OsEngine-master\project\OsEngine\bin\Debug'
    }
}
if (Test-Path -LiteralPath $EngineBin) {
    $EngineBin = (Resolve-Path -LiteralPath $EngineBin).Path
}
if (-not $SkipRealApi) {
    if (-not (Test-Path -LiteralPath (Join-Path $EngineBin 'OsEngine.dll'))) {
        throw 'OsEngine.dll not found. Supply -EngineBin for the intended engine, or use -SkipRealApi for offline-only checks.'
    }
}

$logsRoot = Join-Path $standRoot 'Logs'
[IO.Directory]::CreateDirectory($logsRoot) | Out-Null
$runName = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
if ($Baseline) { $runName += '-baseline' }
$report = Join-Path $logsRoot "$runName.json"
$log = Join-Path $logsRoot "$runName.txt"
$sourceHash = (Get-FileHash -LiteralPath $RobotSource -Algorithm SHA256).Hash
Write-Host "Robot: $RobotSource"
Write-Host "SHA256: $sourceHash"
Write-Host "EngineBin: $EngineBin"
Write-Host "Report: $report"

$standProject = Join-Path $standRoot 'MarketMakerStakan3.TestStand.csproj'
$standArgs = @('run', '--project', $standProject, "-p:RobotSource=$RobotSource", '--verbosity', 'quiet', '--',
    '--module', $Module, '--seed', $Seed.ToString(), '--iterations', $Iterations.ToString(),
    '--source', $RobotSource, '--json', $report)
& dotnet @standArgs 2>&1 | Tee-Object -FilePath $log
$standExit = $LASTEXITCODE

$realExit = 0
if (-not $SkipRealApi) {
    $realProject = Join-Path $standRoot 'RealApi\RealApi.csproj'
    $realArgs = @('run', '--project', $realProject, "-p:RobotSource=$RobotSource", "-p:EngineBin=$EngineBin", '--verbosity', 'quiet')
    & dotnet @realArgs 2>&1 | Tee-Object -FilePath (Join-Path $logsRoot "$runName-real-api.txt")
    $realExit = $LASTEXITCODE
}
$coordinatorProject = Join-Path $standRoot 'Coordinator\Coordinator.csproj'
& dotnet run --project $coordinatorProject --verbosity quiet 2>&1 | Tee-Object -FilePath (Join-Path $logsRoot "$runName-coordinator.txt")
$coordinatorExit = $LASTEXITCODE
Write-Host "Offline exit=$standExit; RealApi exit=$realExit; Coordinator exit=$coordinatorExit"
if ($standExit -ne 0) { exit $standExit }
if ($coordinatorExit -ne 0) { exit $coordinatorExit }
exit $realExit

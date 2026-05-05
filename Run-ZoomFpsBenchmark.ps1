param(
    [string]$OutputPath = ".\\artifacts\\zoom-fps-benchmark.json",
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$outputFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
$outputDirectory = Split-Path -Parent $outputFullPath

if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

if (-not $NoBuild) {
    $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    dotnet build IsoViewport.sln --no-restore
}

Get-Process -Name IsoViewport.Demo -ErrorAction SilentlyContinue | Stop-Process
Get-Process -Name dotnet -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle -like '*IsoViewport*' } |
    Stop-Process

if (Test-Path -LiteralPath $outputFullPath) {
    Remove-Item -LiteralPath $outputFullPath -Force
}

$env:ISOVIEWPORT_AUTOBENCH_OUTPUT = $outputFullPath
$exe = Join-Path $repoRoot 'IsoViewport.Demo\bin\Debug\net10.0\IsoViewport.Demo.exe'
$process = Start-Process -FilePath $exe -PassThru

try {
    $completedInTime = $process.WaitForExit(45000)

    if (-not $completedInTime) {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            throw 'Benchmark timed out after 45 seconds.'
        }

        $process.WaitForExit()
    }

    if (-not (Test-Path -LiteralPath $outputFullPath)) {
        throw "Benchmark completed but did not write output to $outputFullPath"
    }

    $result = Get-Content -LiteralPath $outputFullPath -Raw | ConvertFrom-Json

    if (-not $result.Success) {
        throw "Benchmark failed: $($result.Error)"
    }

    Write-Host "Average FPS: $([math]::Round($result.AverageFps, 2))"
    Write-Host "Min FPS: $([math]::Round($result.MinimumFps, 2))"
    Write-Host "Max FPS: $([math]::Round($result.MaximumFps, 2))"
    Write-Host "Samples: $($result.Samples)"
    Write-Host "Map: $($result.MapRows)x$($result.MapCols)"
    Write-Host "View: $($result.ProjectionMode)"
    Write-Host "Render Mode: $($result.RenderMode)"
    Write-Host "Zoom Range: $([math]::Round($result.ZoomStart, 4)) -> $([math]::Round($result.ZoomEnd, 4))"
    Write-Host "Saved JSON: $outputFullPath"
}
finally {
    Remove-Item Env:ISOVIEWPORT_AUTOBENCH_OUTPUT -ErrorAction SilentlyContinue
}

<#
.SYNOPSIS
Runs an fNbt benchmark with stable Windows-oriented defaults.

.DESCRIPTION
Samples process CPU use for five seconds, rejects nested copies of the benchmark
project, then runs BenchmarkDotNet with fixed affinity and multiple process launches.

.PARAMETER Filter
BenchmarkDotNet method-name filter. This parameter is required.

.PARAMETER Baseline
Optional fNbt package version used by the comparison job.

.PARAMETER BaselineSource
Optional directory containing the baseline package.

.PARAMETER ArtifactsRoot
Directory under which a timestamped artifact directory is created.

.PARAMETER AffinityMask
Benchmark process affinity mask. The default, 256, selects logical CPU 8.

.PARAMETER LaunchCount
Number of benchmark process launches. The default is 5.

.PARAMETER ForceBusy
Continues when the CPU preflight finds a busy process.

.PARAMETER AdditionalArguments
Additional arguments passed to BenchmarkDotNet.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\Run-StableBenchmark.ps1 -Filter '*SaveZLib*'

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\Run-StableBenchmark.ps1 -Filter '*SaveZLib*' -Baseline 1.1.0 -BaselineSource ..\bin\baseline -AdditionalArguments '--statisticalTest', '5%'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string] $Filter,

    [string] $Baseline,

    [string] $BaselineSource,

    [string] $ArtifactsRoot,

    [ValidateScript({ $_ -gt 0 })]
    [uint64] $AffinityMask = 256,

    [ValidateScript({ $_ -gt 0 })]
    [int] $LaunchCount = 5,

    [switch] $ForceBusy,

    [string[]] $AdditionalArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ProcessCpuSnapshot {
    $snapshot = @{}
    foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
        try {
            $snapshot[$process.Id] = [pscustomobject] @{
                Name = $process.ProcessName
                CpuSeconds = [double] $process.CPU
                StartTime = $process.StartTime.ToFileTimeUtc()
            }
        } catch {
            # Protected processes do not expose all accounting fields.
        }
    }
    return $snapshot
}

function Measure-ProcessCpuActivity {
    param(
        [int] $DurationMilliseconds = 5000
    )

    $before = Get-ProcessCpuSnapshot
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Milliseconds $DurationMilliseconds
    $after = Get-ProcessCpuSnapshot
    $stopwatch.Stop()

    $activity = foreach ($entry in $after.GetEnumerator()) {
        $processId = [int] $entry.Key
        $current = $entry.Value
        if ($processId -eq $PID -or -not $before.ContainsKey($processId)) {
            continue
        }

        $previous = $before[$processId]
        if ($previous.StartTime -ne $current.StartTime) {
            continue
        }

        $cpuDelta = $current.CpuSeconds - $previous.CpuSeconds
        if ($cpuDelta -le 0) {
            continue
        }

        [pscustomobject] @{
            Name = $current.Name
            Id = $processId
            CpuSeconds = [math]::Round($cpuDelta, 3)
            OneCorePercent = [math]::Round(100 * $cpuDelta / $stopwatch.Elapsed.TotalSeconds, 1)
        }
    }

    return @($activity | Sort-Object OneCorePercent -Descending)
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Run-StableBenchmark.ps1 supports Windows only.'
}

if ($BaselineSource -and -not $Baseline) {
    throw '-BaselineSource requires -Baseline.'
}

$logicalProcessorCount = [Environment]::ProcessorCount
if ($logicalProcessorCount -lt 64) {
    $firstUnavailableMask = [uint64] [math]::Pow(2, $logicalProcessorCount)
    if ($AffinityMask -ge $firstUnavailableMask) {
        throw "Affinity mask $AffinityMask selects a logical CPU that is not available on this $logicalProcessorCount-processor host."
    }
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$projectPath = Join-Path $scriptRoot 'fNbt.Benchmarks.csproj'
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Benchmark project not found at '$projectPath'."
}

if ($BaselineSource) {
    if (-not (Test-Path -LiteralPath $BaselineSource -PathType Container)) {
        throw "Baseline source directory not found: '$BaselineSource'."
    }
    $BaselineSource = (Resolve-Path -LiteralPath $BaselineSource).Path
}

if (-not $ArtifactsRoot) {
    $ArtifactsRoot = Join-Path $repoRoot 'BenchmarkDotNet.Artifacts'
} else {
    $ArtifactsRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArtifactsRoot)
}
$artifactsPath = Join-Path $ArtifactsRoot (Get-Date -Format 'yyyyMMdd-HHmmss-fff')

Write-Host 'Sampling per-process CPU activity for five seconds...'
$activity = @(Measure-ProcessCpuActivity)
$topProcesses = @($activity | Select-Object -First 10)
if ($topProcesses.Count -gt 0) {
    $topProcesses |
        Select-Object Name, Id, CpuSeconds, @{ Name = 'OneCorePercent'; Expression = { '{0:N1}%' -f $_.OneCorePercent } } |
        Format-Table -AutoSize |
        Out-Host
} else {
    Write-Host 'No measurable process CPU activity was observed.'
}

$busyThreshold = 25.0
$busyProcesses = @($activity | Where-Object OneCorePercent -ge $busyThreshold)
if ($busyProcesses.Count -gt 0) {
    $names = ($busyProcesses | ForEach-Object { '{0} (PID {1}, {2:N1}% of one CPU)' -f $_.Name, $_.Id, $_.OneCorePercent }) -join '; '
    if ($ForceBusy) {
        Write-Warning "Busy-process preflight overridden: $names"
    } else {
        [Console]::Error.WriteLine("CPU preflight failed. Stop or investigate: $names")
        [Console]::Error.WriteLine('Rerun with -ForceBusy only when this contention is intentional.')
    }
} else {
    Write-Host "CPU preflight passed; no sampled readable process averaged $busyThreshold% of one logical CPU."
}

$benchmarkProjects = @(
    Get-ChildItem -LiteralPath $repoRoot -Recurse -Force -File -Filter 'fNbt.Benchmarks.csproj' -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName -Unique |
        Sort-Object
)
$duplicateProjects = $benchmarkProjects.Count -gt 1
if ($duplicateProjects) {
    [Console]::Error.WriteLine('Benchmark project discovery is ambiguous. Found these copies under the repository:')
    foreach ($path in $benchmarkProjects) {
        [Console]::Error.WriteLine("  $path")
    }
    [Console]::Error.WriteLine("Relocate nested worktrees such as '$repoRoot\bin\perf-worktrees' outside the repository, or run from a clean clone.")
}

if (($busyProcesses.Count -gt 0 -and -not $ForceBusy) -or $duplicateProjects) {
    exit 2
}

$benchmarkArguments = @(
    '--filter', $Filter,
    '--launchCount', $LaunchCount.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--affinity', $AffinityMask.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--allStats',
    '--outliers', 'DontRemove',
    '--artifacts', $artifactsPath
)

if ($Baseline) {
    $benchmarkArguments += '--baseline', $Baseline
}
if ($BaselineSource) {
    $benchmarkArguments += '--baseline-source', $BaselineSource
}
if ($AdditionalArguments) {
    $benchmarkArguments += $AdditionalArguments
}

$dotnetArguments = @(
    'run',
    '--configuration', 'Release',
    '--framework', 'net8.0',
    '--project', $projectPath,
    '--no-launch-profile',
    '--'
) + $benchmarkArguments

$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$benchmarkExitCode = 1
$locationChanged = $false

Write-Host "Artifacts: $artifactsPath"
Write-Host "Affinity: $AffinityMask; launches: $LaunchCount"

try {
    Push-Location -LiteralPath $repoRoot
    $locationChanged = $true
    & $dotnet @dotnetArguments
    $benchmarkExitCode = $LASTEXITCODE
} catch {
    [Console]::Error.WriteLine("Benchmark launch failed: $($_.Exception.Message)")
    $benchmarkExitCode = 1
} finally {
    if ($locationChanged) {
        Pop-Location
    }
}

exit $benchmarkExitCode

<#
.SYNOPSIS
Runs a balanced pair of fNbt benchmark comparisons.

.DESCRIPTION
Rejects overlapping runner instances, samples process CPU use for five seconds,
rejects nested copies of the benchmark project, then runs BenchmarkDotNet twice on
logical CPU 8. The controller stays off CPU 8 and its SMT sibling. The second pass
reverses benchmark-case order so package and method ratios are not trusted from one
order. Per-method attributes select launch, warmup, iteration, and outlier settings.

.PARAMETER Filter
One or more BenchmarkDotNet method-name filter patterns. This parameter is required.
Comma-separated values are split into separate patterns, since the powershell -File
boundary flattens arrays into one comma-joined argument.

.PARAMETER Baseline
Optional fNbt package version used by the comparison job.

.PARAMETER BaselineSource
Optional directory containing the baseline package.

.PARAMETER ArtifactsRoot
Directory under which a timestamped artifact directory is created.

.PARAMETER ForceBusy
Continues when the CPU or one-time machine-setup preflight fails.

.PARAMETER AdditionalArguments
Additional arguments passed to BenchmarkDotNet.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\fNbt.Benchmarks\Run-StableBenchmark.ps1 -Filter '*SaveZLib*'

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\fNbt.Benchmarks\Run-StableBenchmark.ps1 -Filter '*SaveZLib*' -Baseline 1.1.1 -BaselineSource .\bin\baseline
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string[]] $Filter,

    [string] $Baseline,

    [string] $BaselineSource,

    [string] $ArtifactsRoot,

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

$Filter = @($Filter | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$AdditionalArguments = @($AdditionalArguments | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

if ($BaselineSource -and -not $Baseline) {
    throw '-BaselineSource requires -Baseline.'
}

$affinityMask = [uint64] 256
$reservedProcessorMask = [int64] 0x300
$logicalProcessorCount = [Environment]::ProcessorCount
if ($logicalProcessorCount -le 8) {
    throw "This machine-specific runner requires logical CPU 8, but only $logicalProcessorCount logical processors are available."
}
$runnerProcess = [Diagnostics.Process]::GetCurrentProcess()
$originalRunnerAffinity = [int64] $runnerProcess.ProcessorAffinity
$controllerAffinity = $originalRunnerAffinity -band (-bnot $reservedProcessorMask)
if ($controllerAffinity -eq 0) {
    $runnerProcess.Dispose()
    throw 'The runner process has no available logical processor outside CPUs 8 and 9.'
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

$benchmarkMutex = [Threading.Mutex]::new($false, 'Global\fNbtBenchmarkRunner')
$ownsBenchmarkMutex = $false
try {
    $ownsBenchmarkMutex = $benchmarkMutex.WaitOne(0)
} catch [Threading.AbandonedMutexException] {
    $ownsBenchmarkMutex = $true
}
if (-not $ownsBenchmarkMutex) {
    $benchmarkMutex.Dispose()
    $runnerProcess.Dispose()
    throw 'Another fNbt benchmark runner is already active on this machine.'
}

$scriptExitCode = 1
$locationChanged = $false
$runnerAffinityChanged = $false
try {
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

$quietingProblems = New-Object 'Collections.Generic.List[string]'
$highPerformanceScheme = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
$activeScheme = (& powercfg.exe /getactivescheme) -join ' '
if ($LASTEXITCODE -ne 0 -or $activeScheme -notmatch [regex]::Escape($highPerformanceScheme)) {
    $quietingProblems.Add('the High Performance power plan is not active')
}
$expectedStoppedServices = @('WSearch', 'SysMain', 'bzserv', 'AdobeARMservice', 'AdobeUpdateService')
$runningServices = @(
    Get-Service -Name $expectedStoppedServices -ErrorAction SilentlyContinue |
        Where-Object Status -eq 'Running'
)
if ($runningServices.Count -gt 0) {
    $quietingProblems.Add("background services are running: $(($runningServices.Name | Sort-Object) -join ', ')")
}
$servicingProcesses = @(Get-Process -Name TiWorker, MoUsoCoreWorker, TrustedInstaller -ErrorAction SilentlyContinue)
if ($servicingProcesses.Count -gt 0) {
    $quietingProblems.Add("Windows servicing is active: $(($servicingProcesses.ProcessName | Sort-Object -Unique) -join ', ')")
}
$interactiveProcessesOnReservedProcessors = foreach ($process in Get-Process -Name Code, codex, node -ErrorAction SilentlyContinue) {
    try {
        if (([int64] $process.ProcessorAffinity -band [int64] 0x300) -ne 0) {
            "$($process.ProcessName) PID $($process.Id)"
        }
    } catch {
        # If affinity cannot be read, the CPU activity check remains the fallback.
    }
}
if (@($interactiveProcessesOnReservedProcessors).Count -gt 0) {
    $quietingProblems.Add("interactive agent processes can run on logical CPUs 8/9: $($interactiveProcessesOnReservedProcessors -join ', ')")
}
if ($quietingProblems.Count -gt 0) {
    $message = $quietingProblems -join '; '
    if ($ForceBusy) {
        Write-Warning "Machine-setup preflight overridden: $message"
    } else {
        [Console]::Error.WriteLine("Machine-setup preflight failed: $message")
        [Console]::Error.WriteLine("Run '$scriptRoot\Quiet-BenchmarkMachine.ps1' once from an elevated prompt.")
    }
} else {
    Write-Host 'Machine-setup preflight passed.'
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

$preflightFailed = (($busyProcesses.Count -gt 0 -or $quietingProblems.Count -gt 0) -and -not $ForceBusy) -or $duplicateProjects
if ($preflightFailed) {
    $scriptExitCode = 2
} else {
    $dotnet = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0].Source

    function Invoke-BenchmarkPass {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Name,

            [switch] $ReverseOrder
        )

        $passArtifactsPath = Join-Path $artifactsPath $Name
        $benchmarkArguments = @('--filter') + $Filter + @(
            '--affinity', $affinityMask.ToString([Globalization.CultureInfo]::InvariantCulture),
            '--allStats',
            '--artifacts', $passArtifactsPath
        )
        if ($Baseline) {
            $benchmarkArguments += '--baseline', $Baseline
            $benchmarkArguments += '--statisticalTest', '5%'
        }
        if ($BaselineSource) {
            $benchmarkArguments += '--baseline-source', $BaselineSource
        }
        if ($ReverseOrder) {
            $benchmarkArguments += '--reverse-order'
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

        Write-Host ''
        Write-Host "Starting $Name pass. Artifacts: $passArtifactsPath"
        & $dotnet @dotnetArguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Name benchmark pass failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host "Balanced artifacts root: $artifactsPath"
    Write-Host "Affinity: $affinityMask (logical CPU 8); launch counts come from each benchmark profile."

    try {
        if ($controllerAffinity -ne $originalRunnerAffinity) {
            $runnerProcess.ProcessorAffinity = [IntPtr] $controllerAffinity
            $runnerAffinityChanged = $true
        }
        Push-Location -LiteralPath $repoRoot
        $locationChanged = $true
        Invoke-BenchmarkPass -Name 'forward-order'
        Invoke-BenchmarkPass -Name 'reverse-order' -ReverseOrder
        $scriptExitCode = 0
    } catch {
        [Console]::Error.WriteLine("Benchmark launch failed: $($_.Exception.Message)")
        $scriptExitCode = 1
    }

    if ($scriptExitCode -eq 0) {
        Write-Host ''
        Write-Host 'Decision rule: require both orders to agree; use 5%/RatioSD<=0.03 for VeryStable and 10%/RatioSD<=0.05 for Average.'
        Write-Host 'Do not gate on multimodal/Unstable timing. Compare exact Allocated_Bytes values in *-measurements.csv.'
    }
}
} finally {
    if ($locationChanged) {
        try {
            Pop-Location
        } catch {
            Write-Warning "Could not restore the working directory: $($_.Exception.Message)"
        }
    }
    if ($runnerAffinityChanged) {
        try {
            $runnerProcess.ProcessorAffinity = [IntPtr] $originalRunnerAffinity
        } catch {
            Write-Warning "Could not restore runner affinity: $($_.Exception.Message)"
        }
    }
    if ($ownsBenchmarkMutex) {
        try {
            $benchmarkMutex.ReleaseMutex()
        } catch {
            Write-Warning "Could not release the benchmark mutex: $($_.Exception.Message)"
        }
    }
    $benchmarkMutex.Dispose()
    $runnerProcess.Dispose()
}

exit $scriptExitCode

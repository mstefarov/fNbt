<#
.SYNOPSIS
Quiets this PC for an fNbt benchmark session.

.DESCRIPTION
Run once from an elevated Windows PowerShell prompt. This machine-specific
script selects the High Performance power plan, stops a short list of automatic
background services, and moves existing VS Code/agent processes away from the
two logical processors reserved for benchmarks.

Networking and Microsoft Defender stay enabled. The script verifies Defender's
supported protection-status fields rather than disabling it. Restart Windows
after the benchmark session to restore the stopped services and process
affinities. The High Performance plan remains selected.
#>
#Requires -Version 5.1
#Requires -RunAsAdministrator

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$highPerformanceScheme = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
$reservedProcessorMask = [int64] 0x300 # logical CPU 8 plus its SMT sibling, CPU 9
$servicesToStop = @(
    'WSearch',
    'SysMain',
    'bzserv',
    'AdobeARMservice',
    'AdobeUpdateService'
)

if ($env:OS -ne 'Windows_NT') {
    throw 'This script supports Windows only.'
}
if ([Environment]::ProcessorCount -le 9) {
    throw 'This machine-specific setup requires logical CPUs 8 and 9.'
}

$servicingProcesses = @(Get-Process -Name TiWorker, MoUsoCoreWorker, TrustedInstaller -ErrorAction SilentlyContinue)
if ($servicingProcesses.Count -gt 0) {
    $names = ($servicingProcesses | ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }) -join ', '
    throw "Windows servicing is active: $names. Wait for it to finish and retry."
}

if (-not (Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue)) {
    throw 'Microsoft Defender status is unavailable; refusing to claim this online machine is protected.'
}
$defender = Get-MpComputerStatus
if (-not [bool] $defender.AMServiceEnabled -or
    -not [bool] $defender.AntivirusEnabled -or
    -not [bool] $defender.RealTimeProtectionEnabled) {
    throw 'Microsoft Defender real-time protection is not fully enabled.'
}
& powercfg.exe /setactive $highPerformanceScheme
if ($LASTEXITCODE -ne 0) {
    throw "powercfg /setactive failed with exit code $LASTEXITCODE."
}
$activeScheme = (& powercfg.exe /getactivescheme) -join ' '
if ($LASTEXITCODE -ne 0 -or $activeScheme -notmatch [regex]::Escape($highPerformanceScheme)) {
    throw 'The High Performance power plan could not be verified.'
}

foreach ($name in $servicesToStop) {
    $service = Get-Service -Name $name -ErrorAction SilentlyContinue
    if (-not $service -or $service.Status -eq 'Stopped') {
        continue
    }
    try {
        Stop-Service -Name $service.Name -ErrorAction Stop
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        Write-Host "Stopped $($service.Name)."
    } catch {
        Write-Warning "Could not stop $($service.Name): $($_.Exception.Message)"
    }
}

foreach ($process in Get-Process -Name Code, codex, node -ErrorAction SilentlyContinue) {
    try {
        $originalMask = [int64] $process.ProcessorAffinity
        $restrictedMask = $originalMask -band (-bnot $reservedProcessorMask)
        if ($restrictedMask -eq 0) {
            Write-Warning "$($process.ProcessName) PID $($process.Id) only uses the reserved processors; its affinity was left unchanged."
            continue
        }
        $process.ProcessorAffinity = [IntPtr] $restrictedMask
        Write-Host "Moved $($process.ProcessName) PID $($process.Id) off logical CPUs 8 and 9."
    } catch {
        Write-Warning "Could not change $($process.ProcessName) PID $($process.Id): $($_.Exception.Message)"
    }
}

Write-Host ''
Write-Host 'Benchmark setup complete. Networking and Microsoft Defender remain enabled.'
Write-Host 'Restart Windows after the session to restore services and process affinities.'

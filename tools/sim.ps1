param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$Spell = 'energybolt',
    [string]$Scenario = 'dummy_line',
    [int]$Ticks = 600,
    [int]$Seed = 1,
    [int]$Stage = 1,
    [string]$Output = 'Builds/sim-result.json'
)
$runeProject = Split-Path -Parent $PSScriptRoot
$runeExecutable = if ([IO.Path]::IsPathRooted($Executable)) { $Executable } else { [IO.Path]::GetFullPath((Join-Path $runeProject $Executable)) }
if (-not (Test-Path -LiteralPath $runeExecutable)) { throw "Windows Player build not found: $runeExecutable" }
$runeOutput = [IO.Path]::GetFullPath((Join-Path $runeProject $Output))
$runeLog = Join-Path $runeProject 'Logs/rune-sim-player.log'
$runeArguments = @('-batchmode', '-nographics', '--sim', '--spell', $Spell, '--scenario', $Scenario, '--ticks', $Ticks, '--seed', $Seed, '--stage', $Stage, '--output', $runeOutput, '-logFile', $runeLog)
& $runeExecutable @runeArguments | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Simulation failed. Inspect $runeLog" }
Get-Content -LiteralPath $runeOutput

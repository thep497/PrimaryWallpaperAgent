[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$')]
    [string]$Label
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$probePath = Get-ProbePath
if (-not (Test-Path -LiteralPath $probePath -PathType Leaf)) {
    throw 'Build the Phase 1 probe before capturing a snapshot.'
}

# The label is supplied by the person running this script on the selected desktop.
# It is NOT an automatically detected Virtual Desktop ID.
$rawJson = & $probePath --json
$probeExitCode = $LASTEXITCODE
if ($probeExitCode -notin @(0, 2)) {
    throw ('Probe failed; no snapshot saved. Exit code: ' + $probeExitCode)
}
$probeSnapshot = ($rawJson | Out-String) | ConvertFrom-Json
if (-not $probeSnapshot.ReadOnly) {
    throw 'Snapshot does not identify itself as a read-only probe.'
}

$capture = [pscustomobject][ordered]@{
    SchemaVersion = 1
    UserSelectedDesktopLabel = $Label
    DesktopIdentification = 'User-provided label; Virtual Desktop ID is not detected'
    ProbeExitCode = $probeExitCode
    Probe = $probeSnapshot
}
$snapshotDirectory = Join-Path $script:ProjectRoot 'snapshots'
New-Item -ItemType Directory -Path $snapshotDirectory -Force | Out-Null
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss_fff', [Globalization.CultureInfo]::InvariantCulture)
$capturePath = Join-Path $snapshotDirectory ('virtual-desktop-{0}-{1}.json' -f $Label, $timestamp)
# CreateNew prevents replacing any existing evidence, even if a filename collides.
$fileStream = [IO.File]::Open($capturePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $jsonBytes = [Text.UTF8Encoding]::new($false).GetBytes(($capture | ConvertTo-Json -Depth 16))
    $fileStream.Write($jsonBytes, 0, $jsonBytes.Length)
}
finally {
    $fileStream.Dispose()
}

Write-Host ('Saved snapshot: ' + $capturePath)
Write-Host ('Declared desktop: ' + $Label + ' (user selected, not detected)')
Write-Host ('Probe exit code: ' + $probeExitCode + ' | Issues: ' + @($probeSnapshot.Issues).Count)
Write-Host ('Topology stable during capture: ' + $probeSnapshot.TopologyStable)
$probeSnapshot.Wallpapers |
    Where-Object { $_.Attached -eq $true } |
    Select-Object GdiName, IsPrimary, MatchStatus, WallpaperPath |
    Format-Table -AutoSize -Wrap
Write-Host 'No wallpaper/display settings were changed. Check the actual images before returning to the chat.'

[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.1')
. (Join-Path $PSScriptRoot 'Common.ps1')
$source = Join-Path $script:ProjectRoot "artifacts\releases\v$Version\win-x64\PrimaryWallpaperAgent.exe"
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Publish the release before testing.' }
$root = Join-Path $script:ProjectRoot ('.local\release-verification-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$copy = Join-Path $root 'Standalone copy ภาษาไทย'
[void][IO.Directory]::CreateDirectory($copy)
$executable = Join-Path $copy 'Renamed Wallpaper Agent.exe'
Copy-Item -LiteralPath $source -Destination $executable
$data = Join-Path $root 'fresh user data'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $executable
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.ArgumentList.Add('--package-check')
$start.ArgumentList.Add($data)
$start.Environment['DOTNET_ROOT'] = Join-Path $root 'no-shared-runtime'
$start.Environment['DOTNET_ROOT_X64'] = Join-Path $root 'no-shared-runtime'
$start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $root 'bundle-cache'
foreach ($run in 1..2) {
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process.WaitForExit(30000)) { throw "Package check timed out; PID=$($process.Id)" }
    if ($process.ExitCode -ne 0) { throw "Package check failed: $($process.ExitCode). Inspect $data\runtime\startup-error.log" }
    $report = Get-Content -LiteralPath (Join-Path $data 'runtime\package-check.json') -Raw | ConvertFrom-Json
    if (-not $report.Passed -or -not $report.SettingsUnchanged -or $report.HasChosenImage -or $report.NativeWallpaperCalls -ne 0 -or $report.RegistryWrites -ne 0) {
        throw 'Read-only first-run check did not meet expectations.'
    }
}
if (@(Get-ChildItem -LiteralPath $copy).Count -ne 1) { throw 'The copied EXE needed sidecar files.' }
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = (Get-Content -LiteralPath (Join-Path (Split-Path $source) 'SHA256SUMS.txt') -Raw).Split(' ')[0]
if ($hash -ne $expected) { throw 'Release checksum does not match.' }
Write-Host "SINGLE-FILE RELEASE CHECK PASSED: copied/renamed EXE, fresh/reopened data, Thai/English, embedded icons, no shared runtime lookup, zero wallpaper/startup writes."
Write-Host "Evidence: $data\runtime\package-check.json"

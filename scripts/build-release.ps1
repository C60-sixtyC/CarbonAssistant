param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\DeskKeeper.App\DeskKeeper.App.csproj"
$releaseRoot = Join-Path $repoRoot "release"
$outDir = Join-Path $releaseRoot "DeskKeeper-$Version-$Runtime"

$releaseRootFull = [IO.Path]::GetFullPath($releaseRoot)
$outDirFull = [IO.Path]::GetFullPath($outDir)
if (-not $outDirFull.StartsWith($releaseRootFull + [IO.Path]::DirectorySeparatorChar)) {
    throw "Refusing to write outside release directory: $outDirFull"
}

$candidates = New-Object System.Collections.Generic.List[string]
if ($env:DOTNET_ROOT) {
    $candidates.Add((Join-Path $env:DOTNET_ROOT "dotnet.exe"))
}
$candidates.Add((Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"))
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCommand) {
    $candidates.Add($dotnetCommand.Source)
}

$dotnet = $null
foreach ($candidate in $candidates) {
    if (-not (Test-Path -LiteralPath $candidate)) {
        continue
    }

    $sdks = & $candidate --list-sdks 2>$null
    if ($LASTEXITCODE -eq 0 -and $sdks) {
        $dotnet = $candidate
        break
    }
}

if (-not $dotnet) {
    throw ".NET 8 SDK not found. Install it or add dotnet to PATH."
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
if (Test-Path -LiteralPath $outDirFull) {
    Remove-Item -LiteralPath $outDirFull -Recurse -Force
}
New-Item -ItemType Directory -Path $outDirFull -Force | Out-Null

$env:NUGET_PACKAGES = Join-Path $repoRoot ".nuget"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

& $dotnet publish $project -c Release -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    "-p:Version=$Version" `
    "-p:AssemblyVersion=$Version.0" `
    "-p:FileVersion=$Version.0" `
    -o $outDirFull
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem -LiteralPath $outDirFull -Filter "*.pdb" -ErrorAction SilentlyContinue |
    Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $repoRoot "docs\RELEASE-zh.md") `
    -Destination (Join-Path $outDirFull "README-zh.txt")
Set-Content -LiteralPath (Join-Path $outDirFull "VERSION.txt") `
    -Value "DeskKeeper $Version ($Runtime)`r`nBuilt: $(Get-Date -Format o)" `
    -Encoding UTF8

$exePath = Join-Path $outDirFull "DeskKeeper.exe"
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $outDirFull "SHA256SUMS.txt") `
    -Value "$hash  DeskKeeper.exe" `
    -Encoding ASCII

$zipPath = Join-Path $releaseRoot "DeskKeeper-$Version-$Runtime.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $outDirFull "*") -DestinationPath $zipPath -CompressionLevel Optimal

$selftestLog = Join-Path $env:TEMP "deskkeeper-release-selftest-$Version.json"
$process = Start-Process -FilePath $exePath `
    -ArgumentList "--selftest", "--log", $selftestLog `
    -Wait -PassThru -WindowStyle Hidden
$selftest = Get-Content -LiteralPath $selftestLog -Raw -Encoding UTF8
if ($process.ExitCode -ne 0 -or $selftest -notmatch '"ok": true') {
    throw "Release selftest failed (exit $($process.ExitCode)): $selftest"
}

Write-Output "Release directory: $outDirFull"
Write-Output "Zip: $zipPath"
Write-Output "SHA256: $hash"
Write-Output "Selftest: PASS"


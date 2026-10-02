[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$DSPGamePath = "D:\Games\DSP",
    [string]$DSPProfilePath = (Join-Path $env:APPDATA "r2modmanPlus-local\DysonSphereProgram\profiles\me-bepinex-5421"),
    [string]$DotNetPath = "dotnet"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\SignalNexus\SignalNexus.csproj"
$manifestPath = Join-Path $repoRoot "package\manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = $manifest.version_number
$packageName = "$($manifest.name)-$version"
$distRoot = Join-Path $repoRoot "dist"
$stage = Join-Path $distRoot $packageName
$zipPath = Join-Path $distRoot "$packageName.zip"

if (-not (Test-Path -LiteralPath (Join-Path $DSPGamePath "DSPGAME_Data\Managed\Assembly-CSharp.dll"))) {
    throw "DSPGamePath does not point to a Dyson Sphere Program installation: $DSPGamePath"
}

if (-not (Test-Path -LiteralPath (Join-Path $DSPProfilePath "BepInEx\core\BepInEx.dll"))) {
    throw "DSPProfilePath does not point to an r2modman profile with BepInEx: $DSPProfilePath"
}

& $DotNetPath build $project -c $Configuration "-p:DSPGamePath=$DSPGamePath" "-p:DSPProfilePath=$DSPProfilePath"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$dllPath = Join-Path $repoRoot "src\SignalNexus\bin\$Configuration\netstandard2.1\SignalNexus.dll"
Copy-Item -LiteralPath $dllPath -Destination (Join-Path $stage "SignalNexus.dll")
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stage "manifest.json")
Copy-Item -LiteralPath (Join-Path $repoRoot "package\icon.png") -Destination (Join-Path $stage "icon.png")
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $stage "README.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "CHANGELOG.md") -Destination (Join-Path $stage "CHANGELOG.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $stage "LICENSE")

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Created $zipPath"

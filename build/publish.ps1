<#
.SYNOPSIS
    Builds a Cosmic Crusade release: tests, the self-contained app and the installer
    artifacts\CosmicCrusade-Setup-<version>.exe.

.DESCRIPTION
    Run from anywhere (paths are relative to the repo root), or double-click publish.bat at the root:
        powershell -ExecutionPolicy Bypass -File build\publish.ps1
    Steps (stops at the first failure):
      1. Reads the version from Directory.Build.props (<Version>).
      2. Runs the tests in CosmicCrusade.sln (Release).
      3. Publishes CosmicCrusade.App self-contained for win-x64 (a folder, not single-file, no trimming) to
         artifacts\app\ (the same settings as the Visual Studio publish profile "Release-win-x64") and checks the
         exe, the licence files and the card data came with it, including the card art. Then publishes the LAN server
         (src\CosmicCrusade.Relay) as one self-contained exe and adds it as artifacts\app\CosmicCrusade-LanServer.exe.
      4. Optionally signs CosmicCrusade.exe (-Sign).
      5. Compiles installer\CosmicCrusade.iss with Inno Setup 6 into artifacts\, then signs the setup (if -Sign) and
         writes its SHA-256 next to it.
    Needs: the .NET 8 SDK and Inno Setup 6 (https://jrsoftware.org/isinfo.php; a build tool, not shipped).

.PARAMETER SkipTests
    Skip the tests (not for real releases).

.PARAMETER AllowMissingArt
    Build even if data\art has no card images (the faces then show without art). For trying the installer out only.

.PARAMETER Sign
    Sign CosmicCrusade.exe and the setup with signtool (needs -CertThumbprint and signtool on PATH).

.PARAMETER InnoCompiler
    Full path to ISCC.exe if Inno Setup 6 isn't in one of its usual folders.
#>
param(
    [switch] $SkipTests,
    [switch] $AllowMissingArt,
    [switch] $Sign,
    [string] $CertThumbprint = '',
    [string] $InnoCompiler = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'

function Step([string] $text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Check-Exit([string] $what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." } }

# ---- 1. Version
[xml] $props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "The version '$version' isn't major.minor.patch." }
Step "Cosmic Crusade $version"

# The card art is left out of zips to keep them small: catch a release built without it.
$artDir = Join-Path $root 'data\art'
$artCount = @(Get-ChildItem $artDir -Filter *.jpg -ErrorAction SilentlyContinue).Count
if ($artCount -eq 0) {
    if ($AllowMissingArt) { Write-Warning 'data\art has no card images: this build will show cards without art.' }
    else { throw "data\art has no card images. Copy the art into data\art (or pass -AllowMissingArt to build anyway)." }
} else {
    Write-Host "Card art: $artCount images"
}

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $artifacts | Out-Null

# ---- 2. Tests
if (-not $SkipTests) {
    Step 'Tests'
    dotnet test (Join-Path $root 'CosmicCrusade.sln') -c Release --nologo
    Check-Exit 'The tests'
}

# ---- 3. The app (self-contained, so players need no .NET runtime)
Step 'CosmicCrusade.App (self-contained win-x64)'
$appOut = Join-Path $artifacts 'app'
dotnet publish (Join-Path $root 'src\CosmicCrusade.App\CosmicCrusade.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishTrimmed=false -p:PublishSingleFile=false -o $appOut --nologo
Check-Exit 'Publishing CosmicCrusade.App'
Get-ChildItem $appOut -Filter *.pdb -Recurse | Remove-Item -Force
foreach ($needed in 'CosmicCrusade.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'data\cards.json', 'data\card-layout.json', 'data\game-modes.json', 'data\online.json', 'data\pools\standard.json', 'docs\HOW-TO-PLAY.md', 'docs\RULEBOOK.md') {
    if (-not (Test-Path (Join-Path $appOut $needed))) { throw "The app publish lacks $needed." }
}
$publishedArt = @(Get-ChildItem (Join-Path $appOut 'data\art') -Filter *.jpg -ErrorAction SilentlyContinue).Count
if ($publishedArt -ne $artCount) { throw "data\art has $artCount images but the publish has $publishedArt." }

# The LAN server: one self-contained exe (it carries its own ASP.NET Core), next to the game
Step 'LAN server (self-contained single file)'
$relayOut = Join-Path $artifacts 'relay'
dotnet publish (Join-Path $root 'src\CosmicCrusade.Relay\CosmicCrusade.Relay.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false -o $relayOut --nologo
Check-Exit 'Publishing the LAN server'
Copy-Item (Join-Path $relayOut 'CosmicCrusade.Relay.exe') (Join-Path $appOut 'CosmicCrusade-LanServer.exe')
$onlineJson = Get-Content (Join-Path $appOut 'data\online.json') -Raw | ConvertFrom-Json
if ($onlineJson.defaultServer) { Write-Host "Default server for this build: $($onlineJson.defaultServer)" }
else { Write-Host 'Default server for this build: none (players type one; set it in data\online.json)' }

# ---- 4. Signing (optional)
function Sign-Files([string[]] $files) {
    if (-not $Sign) { return }
    if (-not $CertThumbprint) { throw '-Sign needs -CertThumbprint.' }
    & signtool sign /sha1 $CertThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $files
    Check-Exit 'signtool'
}
Sign-Files @((Join-Path $appOut 'CosmicCrusade.exe'), (Join-Path $appOut 'CosmicCrusade-LanServer.exe'))

# ---- 5. The installer
Step 'Installer'
if (-not $InnoCompiler) {
    $InnoCompiler = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                      "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $InnoCompiler) {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { $InnoCompiler = $onPath.Source }
}
if (-not $InnoCompiler) { throw 'Inno Setup 6 (ISCC.exe) was not found: install it or pass -InnoCompiler.' }
& $InnoCompiler "/DAppVersion=$version" "/O$artifacts" (Join-Path $root 'installer\CosmicCrusade.iss')
Check-Exit 'Inno Setup'

$setup = Join-Path $artifacts "CosmicCrusade-Setup-$version.exe"
Sign-Files @($setup)
Step "Done: $setup"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host "SHA-256 $hash"
"$hash  CosmicCrusade-Setup-$version.exe" | Set-Content (Join-Path $artifacts "CosmicCrusade-Setup-$version.sha256.txt")

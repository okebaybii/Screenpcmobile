param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "PCToMobile.csproj"
$publishRoot = Join-Path $root "publish"
$publishDirectory = Join-Path $publishRoot "PCToMobile"
$installerDirectory = Join-Path $root "installer"
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$version = [string]$projectXml.Project.PropertyGroup.Version
$scrcpyVersion = "4.0"
$scrcpyArchiveName = "scrcpy-win64-v$scrcpyVersion.zip"
$scrcpyArchiveUrl =
    "https://github.com/Genymobile/scrcpy/releases/download/v$scrcpyVersion/$scrcpyArchiveName"
$scrcpyArchiveHash =
    "75dbeb5b00e6f64292f26f70900ae55ca397786bdfb0b9bbeb481a0549047457"
$toolsSource = Join-Path $root "artifacts\scrcpy-v4\scrcpy-win64-v4.0"
$toolsDestination = Join-Path $publishDirectory "tools"
$licenseSource = Join-Path $root "artifacts\LICENSE-scrcpy.txt"
$qrLicenseSource = Join-Path $root "LICENSE-QRCoder.txt"
$archivePath = Join-Path $publishRoot "PCToMobile-v$version-win-x64.zip"
$setupPath = Join-Path $publishRoot "PCToMobile-Setup-v$version.exe"

if (-not (Test-Path (Join-Path $toolsSource "scrcpy.exe"))) {
    $artifactsDirectory = Join-Path $root "artifacts"
    $scrcpyArchivePath = Join-Path $artifactsDirectory $scrcpyArchiveName
    $scrcpyExtractDirectory = Join-Path $artifactsDirectory "scrcpy-v4"
    New-Item -ItemType Directory -Force -Path $artifactsDirectory | Out-Null

    $downloadRequired = -not (Test-Path $scrcpyArchivePath)
    if (-not $downloadRequired) {
        $downloadRequired =
            (Get-FileHash -LiteralPath $scrcpyArchivePath -Algorithm SHA256).Hash `
                -ne $scrcpyArchiveHash
    }

    if ($downloadRequired) {
        Write-Host "Downloading scrcpy $scrcpyVersion from the official release..."
        Invoke-WebRequest -Uri $scrcpyArchiveUrl -OutFile $scrcpyArchivePath
    }

    $actualHash =
        (Get-FileHash -LiteralPath $scrcpyArchivePath -Algorithm SHA256).Hash
    if ($actualHash -ne $scrcpyArchiveHash) {
        throw "scrcpy archive SHA-256 verification failed."
    }

    New-Item -ItemType Directory -Force -Path $scrcpyExtractDirectory | Out-Null
    Expand-Archive `
        -LiteralPath $scrcpyArchivePath `
        -DestinationPath $scrcpyExtractDirectory `
        -Force
}

if (-not (Test-Path $licenseSource)) {
    New-Item `
        -ItemType Directory `
        -Force `
        -Path (Split-Path -Parent $licenseSource) | Out-Null
    Invoke-WebRequest `
        -Uri "https://raw.githubusercontent.com/Genymobile/scrcpy/v$scrcpyVersion/LICENSE" `
        -OutFile $licenseSource
}

if (-not (Test-Path (Join-Path $toolsSource "scrcpy.exe"))) {
    throw "Không tìm thấy scrcpy tại $toolsSource"
}

$resolvedPublishRoot = [System.IO.Path]::GetFullPath($publishRoot) +
    [System.IO.Path]::DirectorySeparatorChar
$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublishDirectory.StartsWith(
        $resolvedPublishRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Đường dẫn publish nằm ngoài thư mục dự án."
}

if (Test-Path $publishDirectory) {
    $publishedAdb = Join-Path $publishDirectory "tools\adb.exe"
    if (Test-Path $publishedAdb) {
        Start-Process `
            -FilePath $publishedAdb `
            -ArgumentList "kill-server" `
            -WindowStyle Hidden `
            -Wait | Out-Null
        Start-Sleep -Milliseconds 300
    }
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    --configfile (Join-Path $root "NuGet.Config") `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish thất bại với mã $LASTEXITCODE."
}

New-Item -ItemType Directory -Force -Path $toolsDestination | Out-Null
Copy-Item -Path (Join-Path $toolsSource "*") `
    -Destination $toolsDestination `
    -Recurse `
    -Force

Copy-Item -LiteralPath (Join-Path $root "README.md") `
    -Destination $publishDirectory `
    -Force
Copy-Item -LiteralPath (Join-Path $root "THIRD_PARTY_NOTICES.txt") `
    -Destination $publishDirectory `
    -Force
Copy-Item -LiteralPath $licenseSource `
    -Destination $publishDirectory `
    -Force
Copy-Item -LiteralPath $qrLicenseSource `
    -Destination $publishDirectory `
    -Force
Copy-Item `
    -LiteralPath (Join-Path $installerDirectory "Uninstall-PCToMobile.ps1") `
    -Destination $publishDirectory `
    -Force

Compress-Archive -Path (Join-Path $publishDirectory "*") `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal `
    -Force

$iexpress = Join-Path $env:SystemRoot "System32\iexpress.exe"
if (-not (Test-Path $iexpress)) {
    throw "IExpress is required to build the Windows installer."
}

$setupSedPath = Join-Path $publishRoot "PCToMobile-Setup.sed"
$installScriptName = "Install-PCToMobile.ps1"
$archiveName = Split-Path -Leaf $archivePath
$setupSed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%QuietInstallCommand%
UserQuietInstCmd=%QuietInstallCommand%
SourceFiles=SourceFiles

[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$setupPath
FriendlyName=PC to Mobile
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScriptName
PostInstallCmd=<None>
QuietInstallCommand=powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScriptName -Quiet
FILE0=$archiveName
FILE1=$installScriptName

[SourceFiles]
SourceFiles0=$publishRoot\
SourceFiles1=$installerDirectory\

[SourceFiles0]
%FILE0%=

[SourceFiles1]
%FILE1%=
"@

Set-Content -LiteralPath $setupSedPath -Value $setupSed -Encoding Unicode
if (Test-Path $setupPath) {
    Remove-Item -LiteralPath $setupPath -Force
}

$iexpressProcess = Start-Process `
    -FilePath $iexpress `
    -ArgumentList "/N", "/Q", $setupSedPath `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
$setupDeadline = [DateTime]::UtcNow.AddSeconds(10)
while (-not (Test-Path $setupPath) -and
       [DateTime]::UtcNow -lt $setupDeadline) {
    Start-Sleep -Milliseconds 500
}
if ($iexpressProcess.ExitCode -ne 0 -or -not (Test-Path $setupPath)) {
    throw "IExpress failed to create the Windows installer."
}

Write-Host "Portable app: $publishDirectory"
Write-Host "Archive:      $archivePath"
Write-Host "Installer:    $setupPath"

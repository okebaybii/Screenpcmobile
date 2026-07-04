param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
$appName = "PC to Mobile"
$installRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "Programs"))
$installDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $installRoot "PCToMobile"))
$expectedDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "Programs\PCToMobile"))

if (-not $installDirectory.Equals(
        $expectedDirectory,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Invalid installation directory."
}

try {
    $adb = Join-Path $installDirectory "tools\adb.exe"
    if (Test-Path $adb) {
        Start-Process `
            -FilePath $adb `
            -ArgumentList "kill-server" `
            -WindowStyle Hidden `
            -Wait `
            -ErrorAction SilentlyContinue | Out-Null
    }

    Get-Process PCToMobile, scrcpy -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.Path.StartsWith(
                    $installDirectory,
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                $false
            }
        } |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $desktopShortcut = Join-Path (
        [Environment]::GetFolderPath("Desktop")) "$appName.lnk"
    $startMenuShortcut = Join-Path (
        [Environment]::GetFolderPath("Programs")) "$appName.lnk"
    Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $startMenuShortcut -Force -ErrorAction SilentlyContinue
    Remove-Item `
        -LiteralPath "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PCToMobile" `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue

    if (Test-Path $installDirectory) {
        Remove-Item -LiteralPath $installDirectory -Recurse -Force
    }

    if (-not $Quiet) {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show(
            "$appName đã được gỡ khỏi máy.",
            $appName,
            [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]::Information) | Out-Null
    }
}
catch {
    if (-not $Quiet) {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show(
            "Không thể gỡ $appName.`n`n$($_.Exception.Message)",
            $appName,
            [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]::Error) | Out-Null
    }
    exit 1
}

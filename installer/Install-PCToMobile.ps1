param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$appName = "PC to Mobile"
$publisher = "lestmegogo"
$installRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "Programs"))
$installDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $installRoot "PCToMobile"))
$expectedPrefix = $installRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

if (-not $installDirectory.StartsWith(
        $expectedPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Invalid installation directory."
}

function Stop-InstalledProcesses {
    param([string]$Directory)

    $adb = Join-Path $Directory "tools\adb.exe"
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
                    $Directory,
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                $false
            }
        } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function New-AppShortcut {
    param(
        [string]$ShortcutPath,
        [string]$ExecutablePath,
        [string]$WorkingDirectory
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $ExecutablePath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$ExecutablePath,0"
    $shortcut.Description = "Phản chiếu và điều khiển thiết bị Android"
    $shortcut.Save()
}

try {
    $package = Get-ChildItem `
        -Path (Join-Path $PSScriptRoot "PCToMobile-v*-win-x64.zip") |
        Select-Object -First 1
    if ($null -eq $package) {
        throw "Không tìm thấy gói ứng dụng trong bộ cài."
    }

    Stop-InstalledProcesses -Directory $installDirectory

    if (Test-Path $installDirectory) {
        Remove-Item -LiteralPath $installDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
    Expand-Archive `
        -LiteralPath $package.FullName `
        -DestinationPath $installDirectory `
        -Force

    $executable = Join-Path $installDirectory "PCToMobile.exe"
    if (-not (Test-Path $executable)) {
        throw "Bộ cài không chứa PCToMobile.exe."
    }

    $desktopShortcut = Join-Path (
        [Environment]::GetFolderPath("Desktop")) "$appName.lnk"
    $startMenuShortcut = Join-Path (
        [Environment]::GetFolderPath("Programs")) "$appName.lnk"
    New-AppShortcut $desktopShortcut $executable $installDirectory
    New-AppShortcut $startMenuShortcut $executable $installDirectory

    $version = (Get-Item $executable).VersionInfo.ProductVersion
    $estimatedSize = [int](
        (Get-ChildItem $installDirectory -File -Recurse |
            Measure-Object Length -Sum).Sum / 1KB)
    $uninstallScript = Join-Path $installDirectory "Uninstall-PCToMobile.ps1"
    $uninstallCommand =
        "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`""
    $uninstallKey =
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PCToMobile"

    New-Item -Path $uninstallKey -Force | Out-Null
    New-ItemProperty $uninstallKey DisplayName `
        -Value $appName -PropertyType String -Force | Out-Null
    New-ItemProperty $uninstallKey DisplayVersion `
        -Value $version -PropertyType String -Force | Out-Null
    New-ItemProperty $uninstallKey Publisher `
        -Value $publisher -PropertyType String -Force | Out-Null
    New-ItemProperty $uninstallKey InstallLocation `
        -Value $installDirectory -PropertyType String -Force | Out-Null
    New-ItemProperty $uninstallKey DisplayIcon `
        -Value "$executable,0" -PropertyType String -Force | Out-Null
    New-ItemProperty $uninstallKey UninstallString `
        -Value $uninstallCommand -PropertyType String -Force | Out-Null
    New-ItemProperty $uninstallKey QuietUninstallString `
        -Value "$uninstallCommand -Quiet" -PropertyType String -Force | Out-Null
    New-ItemProperty $uninstallKey EstimatedSize `
        -Value $estimatedSize -PropertyType DWord -Force | Out-Null
    New-ItemProperty $uninstallKey NoModify `
        -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $uninstallKey NoRepair `
        -Value 1 -PropertyType DWord -Force | Out-Null

    Start-Process -FilePath $executable -WorkingDirectory $installDirectory

    if (-not $Quiet) {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show(
            "$appName đã được cài đặt thành công.",
            $appName,
            [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]::Information) | Out-Null
    }
}
catch {
    if (-not $Quiet) {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show(
            "Không thể cài đặt $appName.`n`n$($_.Exception.Message)",
            $appName,
            [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]::Error) | Out-Null
    }
    else {
        Write-Error $_
    }
    exit 1
}

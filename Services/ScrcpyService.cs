using System.Diagnostics;
using System.IO;
using PCToMobile.Models;

namespace PCToMobile.Services;

public sealed class ScrcpyService : IDisposable
{
    private readonly ToolLocator _tools;
    private readonly object _sessionLock = new();
    private readonly Dictionary<string, Process> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<string>? SessionEnded;

    public ScrcpyService(ToolLocator tools)
    {
        _tools = tools;
    }

    public bool IsRunning(string serial)
    {
        lock (_sessionLock)
        {
            return _sessions.TryGetValue(serial, out var process) && !process.HasExited;
        }
    }

    public void Start(AndroidDevice device, MirrorOptions options)
    {
        if (!_tools.HasScrcpy)
        {
            throw new FileNotFoundException(
                "Không tìm thấy scrcpy.exe. Hãy đặt bản Windows của scrcpy vào thư mục tools.");
        }

        if (!device.IsReady)
        {
            throw new InvalidOperationException(
                "Thiết bị chưa sẵn sàng. Hãy xác nhận quyền USB debugging trên điện thoại.");
        }

        if (IsRunning(device.Serial))
        {
            throw new InvalidOperationException("Thiết bị này đang được điều khiển.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _tools.ScrcpyPath!,
            WorkingDirectory = Path.GetDirectoryName(_tools.ScrcpyPath)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Add(startInfo, "--serial", device.Serial);
        Add(startInfo, "--window-title", $"PC to Mobile - {device.DisplayName}");
        Add(startInfo, "--max-size", options.MaxSize.ToString());
        Add(startInfo, "--max-fps", options.MaxFps.ToString());
        Add(startInfo, "--video-bit-rate", $"{options.BitRateMbps}M");

        if (!options.ForwardAudio)
        {
            startInfo.ArgumentList.Add("--no-audio");
        }

        if (options.StayAwake)
        {
            startInfo.ArgumentList.Add("--stay-awake");
        }

        if (options.TurnScreenOff)
        {
            startInfo.ArgumentList.Add("--turn-screen-off");
        }

        if (!string.IsNullOrWhiteSpace(options.RecordPath))
        {
            Add(startInfo, "--record", options.RecordPath);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Exited += (_, _) =>
        {
            bool ownsProcess;
            lock (_sessionLock)
            {
                ownsProcess = _sessions.Remove(device.Serial);
            }

            if (ownsProcess)
            {
                process.Dispose();
            }

            SessionEnded?.Invoke(this, device.Serial);
        };

        lock (_sessionLock)
        {
            _sessions[device.Serial] = process;
        }

        try
        {
            process.Start();
        }
        catch
        {
            lock (_sessionLock)
            {
                _sessions.Remove(device.Serial);
            }
            process.Dispose();
            throw;
        }
    }

    public void Stop(string serial)
    {
        Process? process;
        lock (_sessionLock)
        {
            _sessions.Remove(serial, out process);
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(1500))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    public void StopUnavailableSessions(IEnumerable<AndroidDevice> devices)
    {
        var readySerials = devices
            .Where(device => device.IsReady)
            .Select(device => device.Serial)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] unavailableSerials;
        lock (_sessionLock)
        {
            unavailableSerials = _sessions.Keys
                .Where(serial => !readySerials.Contains(serial))
                .ToArray();
        }

        foreach (var serial in unavailableSerials)
        {
            Stop(serial);
        }
    }

    public void StopAll()
    {
        string[] serials;
        lock (_sessionLock)
        {
            serials = _sessions.Keys.ToArray();
        }

        foreach (var serial in serials)
        {
            Stop(serial);
        }
    }

    public void Dispose()
    {
        StopAll();
    }

    private static void Add(ProcessStartInfo info, string name, string value)
    {
        info.ArgumentList.Add(name);
        info.ArgumentList.Add(value);
    }
}

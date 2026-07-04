using System.IO;

namespace PCToMobile.Services;

public sealed class ToolLocator
{
    public string? AdbPath { get; private set; }
    public string? ScrcpyPath { get; private set; }

    public bool HasAdb => AdbPath is not null;
    public bool HasScrcpy => ScrcpyPath is not null;
    public bool IsReady => HasAdb && HasScrcpy;

    public void Refresh()
    {
        AdbPath = FindExecutable("adb.exe");
        ScrcpyPath = FindExecutable("scrcpy.exe");
    }

    private static string? FindExecutable(string fileName)
    {
        string[] localCandidates =
        [
            Path.Combine(AppContext.BaseDirectory, "tools", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Environment.CurrentDirectory, "tools", fileName),
            Path.Combine(Environment.CurrentDirectory, fileName)
        ];

        foreach (var candidate in localCandidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore malformed PATH entries and continue searching.
            }
        }

        return null;
    }
}

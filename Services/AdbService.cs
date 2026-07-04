using System.IO;
using System.Text.RegularExpressions;
using PCToMobile.Models;

namespace PCToMobile.Services;

public sealed partial class AdbService
{
    private readonly ToolLocator _tools;

    public AdbService(ToolLocator tools)
    {
        _tools = tools;
    }

    public async Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        var result = await ProcessRunner.RunAsync(
            _tools.AdbPath!,
            ["devices", "-l"],
            cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? "ADB không thể đọc danh sách thiết bị."
                    : result.StandardError.Trim());
        }

        var devices = new List<AndroidDevice>();
        foreach (var rawLine in result.StandardOutput.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*'))
            {
                continue;
            }

            var match = DeviceLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var details = ParseDetails(match.Groups["details"].Value);
            devices.Add(new AndroidDevice
            {
                Serial = match.Groups["serial"].Value,
                State = match.Groups["state"].Value,
                Model = details.GetValueOrDefault("model", "Android"),
                Product = details.GetValueOrDefault("product", string.Empty),
                Transport = details.GetValueOrDefault("transport_id", string.Empty)
            });
        }

        return await RemoveDuplicateWirelessAliasesAsync(devices, cancellationToken);
    }

    public Task<CommandResult> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return ProcessRunner.RunAsync(
            _tools.AdbPath!,
            ["connect", NormalizeEndpoint(endpoint, 5555)],
            cancellationToken);
    }

    public Task<CommandResult> PairAsync(
        string endpoint,
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        if (string.IsNullOrWhiteSpace(pairingCode))
        {
            throw new ArgumentException("Hãy nhập mã ghép đôi trên điện thoại.", nameof(pairingCode));
        }

        return ProcessRunner.RunAsync(
            _tools.AdbPath!,
            ["pair", NormalizeEndpoint(endpoint, null), pairingCode.Trim()],
            cancellationToken);
    }

    public Task<CommandResult> DisconnectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return ProcessRunner.RunAsync(
            _tools.AdbPath!,
            ["disconnect", serial],
            cancellationToken);
    }

    public async Task<IReadOnlyList<MdnsService>> GetMdnsServicesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        var result = await ProcessRunner.RunAsync(
            _tools.AdbPath!,
            ["mdns", "services"],
            cancellationToken);

        if (!result.Success)
        {
            return [];
        }

        var services = new List<MdnsService>();
        foreach (var rawLine in result.StandardOutput.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var match = MdnsLineRegex().Match(rawLine.Trim());
            if (match.Success)
            {
                services.Add(new MdnsService(
                    match.Groups["name"].Value,
                    match.Groups["type"].Value,
                    match.Groups["endpoint"].Value));
            }
        }

        return services;
    }

    private void EnsureAvailable()
    {
        if (!_tools.HasAdb)
        {
            throw new FileNotFoundException(
                "Không tìm thấy adb.exe. Hãy đặt Android Platform Tools vào thư mục tools.");
        }
    }

    private static Dictionary<string, string> ParseDetails(string input)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DetailRegex().Matches(input))
        {
            details[match.Groups["key"].Value] = match.Groups["value"].Value;
        }

        return details;
    }

    private static string NormalizeEndpoint(string endpoint, int? defaultPort)
    {
        var value = endpoint.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Hãy nhập địa chỉ IP của điện thoại.", nameof(endpoint));
        }

        if (value.Contains(':', StringComparison.Ordinal) || defaultPort is null)
        {
            return value;
        }

        return $"{value}:{defaultPort}";
    }

    private async Task<IReadOnlyList<AndroidDevice>> RemoveDuplicateWirelessAliasesAsync(
        List<AndroidDevice> devices,
        CancellationToken cancellationToken)
    {
        var mdnsAliases = devices
            .Where(device =>
                device.Serial.Contains(
                    "._adb-tls-connect._tcp",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (mdnsAliases.Length == 0)
        {
            return devices;
        }

        var services = await GetMdnsServicesAsync(cancellationToken);
        var duplicateAliases = services
            .Where(service =>
                service.Type.Equals(
                    "_adb-tls-connect._tcp",
                    StringComparison.OrdinalIgnoreCase))
            .Where(service => devices.Any(device =>
                device.Serial.Equals(service.Endpoint, StringComparison.OrdinalIgnoreCase)))
            .Select(service => $"{service.Name}.{service.Type}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (duplicateAliases.Count == 0)
        {
            return devices;
        }

        return devices
            .Where(device => !duplicateAliases.Contains(device.Serial))
            .ToArray();
    }

    [GeneratedRegex(@"^(?<serial>\S+)\s+(?<state>\S+)(?<details>.*)$")]
    private static partial Regex DeviceLineRegex();

    [GeneratedRegex(@"(?<key>[a-zA-Z_]+):(?<value>\S+)")]
    private static partial Regex DetailRegex();

    [GeneratedRegex(@"^(?<name>\S+)\s+(?<type>_adb(?:-tls-(?:pairing|connect))?\._tcp)\s+(?<endpoint>\S+)$")]
    private static partial Regex MdnsLineRegex();
}

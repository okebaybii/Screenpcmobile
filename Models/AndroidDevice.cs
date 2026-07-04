namespace PCToMobile.Models;

public sealed class AndroidDevice
{
    public required string Serial { get; init; }
    public required string State { get; init; }
    public string Model { get; init; } = "Android";
    public string Product { get; init; } = string.Empty;
    public string Transport { get; init; } = string.Empty;

    public bool IsReady => State.Equals("device", StringComparison.OrdinalIgnoreCase);
    public bool IsWireless =>
        Serial.Contains(':', StringComparison.Ordinal) ||
        Serial.Contains("._adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase);

    public string DisplayName =>
        IsReady ? Model.Replace('_', ' ') :
        State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase) ? "Chưa cấp quyền" :
        State.Equals("offline", StringComparison.OrdinalIgnoreCase) ? "Thiết bị offline" :
        "Thiết bị Android";

    public string ConnectionLabel =>
        IsWireless ? "Wi-Fi" : "USB";
}

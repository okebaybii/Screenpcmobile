namespace PCToMobile.Models;

public sealed record QrPairingResult(
    bool Success,
    string Message,
    string? PairingEndpoint = null,
    string? ConnectEndpoint = null);

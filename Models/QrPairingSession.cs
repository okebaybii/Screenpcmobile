namespace PCToMobile.Models;

public sealed record QrPairingSession(
    string ServiceName,
    string Secret,
    string Payload,
    byte[] QrPng);

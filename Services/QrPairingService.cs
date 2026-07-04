using System.Security.Cryptography;
using PCToMobile.Models;
using QRCoder;

namespace PCToMobile.Services;

public sealed class QrPairingService
{
    private const string RandomAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    private readonly AdbService _adb;

    public QrPairingService(AdbService adb)
    {
        _adb = adb;
    }

    public QrPairingSession CreateSession()
    {
        var serviceName = $"studio-{CreateRandomText(10)}";
        var secret = CreateRandomText(12);
        var payload = $"WIFI:T:ADB;S:{serviceName};P:{secret};;";
        var qrPng = PngByteQRCodeHelper.GetQRCode(
            payload,
            QRCodeGenerator.ECCLevel.M,
            16);

        return new QrPairingSession(serviceName, secret, payload, qrPng);
    }

    public async Task<QrPairingResult> PairAsync(
        QrPairingSession session,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Đang chờ điện thoại quét mã QR...");

        MdnsService? pairingService = null;
        for (var attempt = 0; attempt < 90; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var services = await _adb.GetMdnsServicesAsync(cancellationToken);
            pairingService = services.FirstOrDefault(service =>
                service.Type.Equals(
                    "_adb-tls-pairing._tcp",
                    StringComparison.OrdinalIgnoreCase) &&
                service.Name.Equals(
                    session.ServiceName,
                    StringComparison.Ordinal));

            if (pairingService is not null)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        if (pairingService is null)
        {
            return new QrPairingResult(
                false,
                "Hết thời gian chờ quét QR. Hãy tạo mã mới.");
        }

        progress?.Report("Đã nhận điện thoại, đang ghép đôi...");
        var pairResult = await _adb.PairAsync(
            pairingService.Endpoint,
            session.Secret,
            cancellationToken);

        if (!pairResult.Success ||
            !pairResult.BestMessage.Contains(
                "Successfully paired",
                StringComparison.OrdinalIgnoreCase))
        {
            return new QrPairingResult(
                false,
                string.IsNullOrWhiteSpace(pairResult.BestMessage)
                    ? "Ghép đôi QR không thành công."
                    : pairResult.BestMessage,
                pairingService.Endpoint);
        }

        progress?.Report("Đã ghép đôi, đang kết nối...");
        MdnsService? connectService = null;
        for (var attempt = 0; attempt < 15; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var services = await _adb.GetMdnsServicesAsync(cancellationToken);
            connectService = services.FirstOrDefault(service =>
                service.Type.Equals(
                    "_adb-tls-connect._tcp",
                    StringComparison.OrdinalIgnoreCase) &&
                service.Host.Equals(
                    pairingService.Host,
                    StringComparison.OrdinalIgnoreCase));

            if (connectService is not null)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        if (connectService is not null)
        {
            await _adb.ConnectAsync(connectService.Endpoint, cancellationToken);
        }

        return new QrPairingResult(
            true,
            "Ghép đôi QR thành công.",
            pairingService.Endpoint,
            connectService?.Endpoint);
    }

    private static string CreateRandomText(int length)
    {
        return string.Create(length, RandomAlphabet, static (buffer, alphabet) =>
        {
            for (var index = 0; index < buffer.Length; index++)
            {
                buffer[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }
        });
    }
}

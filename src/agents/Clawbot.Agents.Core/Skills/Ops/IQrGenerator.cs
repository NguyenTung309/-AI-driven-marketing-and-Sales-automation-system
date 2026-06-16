using QRCoder;

namespace Clawbot.Agents.Core.Skills.Ops;

public sealed record QrSpec(string Payload, int SizePx, string EccLevel);

public sealed record QrImage(byte[] PngBytes, int Width, int Height);

public interface IQrGenerator : ISkill
{
    Task<QrImage> GenerateAsync(QrSpec spec, CancellationToken ct);
}

internal sealed class QRCoderGenerator : IQrGenerator
{
    public string Name => "qr-code-generator";

    public Task<QrImage> GenerateAsync(QrSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Payload);

        var eccLevel = spec.EccLevel.ToUpperInvariant() switch
        {
            "L" => QRCodeGenerator.ECCLevel.L,
            "M" => QRCodeGenerator.ECCLevel.M,
            "Q" => QRCodeGenerator.ECCLevel.Q,
            "H" => QRCodeGenerator.ECCLevel.H,
            _ => QRCodeGenerator.ECCLevel.M,
        };

        var size = spec.SizePx is > 0 and <= 1000 ? spec.SizePx : 200;

        using var qrGen = new QRCodeGenerator();
        using var qrData = qrGen.CreateQrCode(spec.Payload, eccLevel);
        using var pngQr = new PngByteQRCode(qrData);
        var pngBytes = pngQr.GetGraphic(size / qrData.ModuleMatrix.Count);

        return Task.FromResult(new QrImage(pngBytes, size, size));
    }
}

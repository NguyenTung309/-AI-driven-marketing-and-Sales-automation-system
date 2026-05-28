namespace Clawbot.Agents.Core.Skills.Ops;

public sealed record QrSpec(string Payload, int SizePx, string EccLevel);   // L|M|Q|H

public sealed record QrImage(byte[] PngBytes, int Width, int Height);

public interface IQrGenerator : ISkill
{
    Task<QrImage> GenerateAsync(QrSpec spec, CancellationToken ct);
}

// Source: https://www.nuget.org/packages/QRCoder  (MIT license).
internal sealed class QRCoderGenerator : IQrGenerator
{
    public string Name => "qr-code-generator";

    public Task<QrImage> GenerateAsync(QrSpec spec, CancellationToken ct) =>
        throw new NotImplementedException("TODO: QRCoder.QRCodeGenerator + PngByteQRCode.GetGraphic.");
}

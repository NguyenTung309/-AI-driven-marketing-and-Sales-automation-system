using System.Security.Cryptography;
using System.Text;
using Clawbot.SharedKernel.Security;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Security;

public sealed class EncryptionOptions
{
    public string Base64Key { get; set; } = string.Empty;
}

public sealed class AesEncryptor(IOptions<EncryptionOptions> options) : IEncryptor
{
    private const byte Version = 1;
    private const int IvLength = 16;
    private const int TagLength = 32;

    private readonly byte[] _key = Convert.FromBase64String(options.Value.Base64Key);

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = enc.TransformFinalBlock(bytes, 0, bytes.Length);
        var body = new byte[1 + aes.IV.Length + cipher.Length];
        body[0] = Version;
        Buffer.BlockCopy(aes.IV, 0, body, 1, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, body, 1 + aes.IV.Length, cipher.Length);

        var tag = ComputeTag(body);
        var output = new byte[body.Length + tag.Length];
        Buffer.BlockCopy(body, 0, output, 0, body.Length);
        Buffer.BlockCopy(tag, 0, output, body.Length, tag.Length);
        return Convert.ToBase64String(output);
    }

    public string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        if (data.Length >= 1 + IvLength + TagLength && data[0] == Version)
            return DecryptAuthenticated(data);

        return DecryptLegacy(data);
    }

    private string DecryptAuthenticated(byte[] data)
    {
        var bodyLength = data.Length - TagLength;
        var body = data.AsSpan(0, bodyLength).ToArray();
        var expectedTag = data.AsSpan(bodyLength, TagLength);
        var actualTag = ComputeTag(body);
        if (!CryptographicOperations.FixedTimeEquals(actualTag, expectedTag))
            throw new CryptographicException("Ciphertext authentication failed.");

        using var aes = Aes.Create();
        aes.Key = _key;
        var iv = new byte[IvLength];
        Buffer.BlockCopy(data, 1, iv, 0, IvLength);
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        var cipherOffset = 1 + IvLength;
        return Encoding.UTF8.GetString(dec.TransformFinalBlock(data, cipherOffset, bodyLength - cipherOffset));
    }

    private string DecryptLegacy(byte[] data)
    {
        if (data.Length <= IvLength)
            throw new CryptographicException("Ciphertext is too short.");

        using var aes = Aes.Create();
        aes.Key = _key;
        var iv = new byte[IvLength];
        Buffer.BlockCopy(data, 0, iv, 0, IvLength);
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        return Encoding.UTF8.GetString(dec.TransformFinalBlock(data, IvLength, data.Length - IvLength));
    }

    private byte[] ComputeTag(byte[] body)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(body);
    }
}

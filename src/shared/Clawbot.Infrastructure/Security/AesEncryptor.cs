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
    private readonly byte[] _key = Convert.FromBase64String(options.Value.Base64Key);

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = enc.TransformFinalBlock(bytes, 0, bytes.Length);
        var output = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, output, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, output, aes.IV.Length, cipher.Length);
        return Convert.ToBase64String(output);
    }

    public string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        using var aes = Aes.Create();
        aes.Key = _key;
        var iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, 16);
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        return Encoding.UTF8.GetString(dec.TransformFinalBlock(data, 16, data.Length - 16));
    }
}

namespace Clawbot.SharedKernel.Security;

public interface IEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

public interface IAuthenticatedEncryptor : IEncryptor
{
    string DecryptAuthenticated(string ciphertext);
}

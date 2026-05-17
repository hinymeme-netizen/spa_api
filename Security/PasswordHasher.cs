using System.Security.Cryptography;

namespace SpaApi.Security;

public sealed class PasswordHasher : IPasswordHasher
{
  private const int SaltSize = 16;
  private const int KeySize = 32;
  private const int Iterations = 100_000;

  public string Hash(string password)
  {
    var salt = RandomNumberGenerator.GetBytes(SaltSize);
    var key = Rfc2898DeriveBytes.Pbkdf2(
      password,
      salt,
      Iterations,
      HashAlgorithmName.SHA256,
      KeySize);

    return string.Join('.', "PBKDF2", Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
  }

  public bool Verify(string password, string passwordHash)
  {
    var parts = passwordHash.Split('.', 4);
    if (parts.Length != 4) return false;
    if (!string.Equals(parts[0], "PBKDF2", StringComparison.OrdinalIgnoreCase)) return false;
    if (!int.TryParse(parts[1], out var iter)) return false;

    var salt = Convert.FromBase64String(parts[2]);
    var key = Convert.FromBase64String(parts[3]);

    var keyToCheck = Rfc2898DeriveBytes.Pbkdf2(
      password,
      salt,
      iter,
      HashAlgorithmName.SHA256,
      key.Length);

    return CryptographicOperations.FixedTimeEquals(keyToCheck, key);
  }
}


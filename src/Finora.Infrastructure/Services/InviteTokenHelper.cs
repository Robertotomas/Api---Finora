using System.Security.Cryptography;
using System.Text;

namespace Finora.Infrastructure.Services;

public static class InviteTokenHelper
{
    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    public static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string GenerateOtp() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0 || at >= email.Length - 1)
            return "***";
        var local = email[..at];
        var domain = email[(at + 1)..];
        var prefix = local.Length <= 1 ? local : local[0] + "***";
        return $"{prefix}@{domain}";
    }
}

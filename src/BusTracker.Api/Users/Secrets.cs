using System.Security.Cryptography;
using System.Text;

namespace BusTracker.Api.Users;

public static class Secrets
{
    private const string LinkCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NewDeviceKey() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string NewLinkCode()
    {
        var chars = Enumerable.Range(0, 6).Select(_ => LinkCodeAlphabet[RandomNumberGenerator.GetInt32(LinkCodeAlphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 3)}-{new string(chars, 3, 3)}";
    }

    public static string NormalizeLinkCode(string code) =>
        new string(code.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()) is { Length: 6 } compact
            ? $"{compact[..3]}-{compact[3..]}"
            : "";

    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool MatchesAny(string candidate, IEnumerable<string> allowed)
    {
        var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Trim()));
        var matched = false;
        foreach (var code in allowed.Where(c => !string.IsNullOrWhiteSpace(c)))
            matched |= CryptographicOperations.FixedTimeEquals(candidateHash, SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));
        return matched;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

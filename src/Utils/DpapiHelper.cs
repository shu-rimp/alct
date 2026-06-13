using System.Security.Cryptography;
using System.Text;

namespace AlctClient.Utils;

internal static class DpapiHelper
{
    private const string _prefix = "dpapi:";

    internal static string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
        return _prefix + Convert.ToBase64String(encrypted);
    }

    internal static string Decrypt(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(_prefix)) return value;
        var decrypted = ProtectedData.Unprotect(
            Convert.FromBase64String(value[_prefix.Length..]), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}

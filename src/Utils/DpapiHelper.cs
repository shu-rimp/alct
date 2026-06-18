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
        try
        {
            var decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(value[_prefix.Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            // 정상적인 사용에서 이 예외가 터질일은 없지만
            // 사용자가 직접 appsettings.json을 복사해서 다른 pc에 넣을때 일어날 수 있음.
            Logger.Error("Dpapi", ex);
            return string.Empty;
        }
    }
}

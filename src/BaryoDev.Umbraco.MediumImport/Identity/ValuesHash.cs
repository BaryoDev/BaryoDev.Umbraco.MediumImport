using System.Security.Cryptography;
using System.Text;

namespace BaryoDev.Umbraco.MediumImport.Identity;

public static class ValuesHash
{
    public static string Compute(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sb = new StringBuilder();
        foreach (var key in values.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            sb.Append(key).Append('\0').Append(values[key] ?? string.Empty).Append('\0');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}

using System.Globalization;
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

    /// <summary>
    /// A date as Umbraco stores it: whole seconds, no kind. Umbraco saves a Date property through
    /// value.ToString() and parses it back, so the database never keeps milliseconds or the Utc kind,
    /// while a read of the node just saved can still return the value as it was assigned.
    /// </summary>
    public static string Date(DateTime value) =>
        new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Unspecified)
            .ToString("o", CultureInfo.InvariantCulture);
}

using System.Text.RegularExpressions;

namespace BaryoDev.Umbraco.MediumImport.Conversion;

/// <summary>Turns a language hint into a value that is safe to use in a class name, or null.</summary>
public static class CodeLanguage
{
    private static readonly Regex Valid = new(@"^[a-z0-9+#-]{1,30}\z", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.Ordinal)
    {
        [".cs"] = "csharp", [".go"] = "go", [".js"] = "javascript", [".ts"] = "typescript",
        [".py"] = "python", [".sh"] = "bash", [".json"] = "json", [".yml"] = "yaml", [".yaml"] = "yaml",
        [".sql"] = "sql", [".html"] = "html", [".css"] = "css", [".xml"] = "xml", [".java"] = "java",
        [".kt"] = "kotlin", [".rb"] = "ruby", [".rs"] = "rust", [".swift"] = "swift",
    };

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string value = raw.Trim().ToLowerInvariant().Replace(' ', '-');
        return Valid.IsMatch(value) ? value : null;
    }

    public static string? FromFileName(string name) =>
        Extensions.TryGetValue(Path.GetExtension(name).ToLowerInvariant(), out string? language) ? language : null;
}

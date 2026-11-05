using System.Text.Json;
using System.Text.Json.Serialization;
using BaryoDev.Umbraco.MediumImport.Engine;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.MediumImport.Settings;

/// <summary>Remembers the last import settings in Umbraco's key-value table.</summary>
public sealed class SettingsStore(IKeyValueService keyValues)
{
    public const string Key = "BaryoDev.Umbraco.MediumImport.Settings";

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public ImportSettings? Load()
    {
        string? raw = keyValues.GetValue(Key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ImportSettings>(raw, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(ImportSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        keyValues.SetValue(Key, JsonSerializer.Serialize(s, Json));
    }
}

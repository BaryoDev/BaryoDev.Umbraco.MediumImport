using BaryoDev.Umbraco.MediumImport.Conversion;

namespace BaryoDev.Umbraco.MediumImport.Net;

public interface IGistClient
{
    /// <summary>Reads a public gist. Null on any failure.</summary>
    Task<Gist?> GetAsync(string gistId, CancellationToken ct);
}

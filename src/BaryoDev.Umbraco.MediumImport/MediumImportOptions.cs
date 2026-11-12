namespace BaryoDev.Umbraco.MediumImport;

public sealed class MediumImportOptions
{
    public const string Section = "BaryoDev:MediumImport";

    public long MaxUploadBytes { get; set; } = 100 * 1024 * 1024;
    public long MaxEntryBytes { get; set; } = 20 * 1024 * 1024;
    public long MaxTotalBytes { get; set; } = 100 * 1024 * 1024;
    public int MaxEntries { get; set; } = 5000;
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxGistBytes { get; set; } = 1024 * 1024;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public TimeSpan UploadTtl { get; set; } = TimeSpan.FromHours(1);
}

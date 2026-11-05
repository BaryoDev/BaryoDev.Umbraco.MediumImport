namespace BaryoDev.Umbraco.MediumImport.Parsing;

public sealed record MediumExportLimits(long MaxEntryBytes, long MaxTotalBytes, int MaxEntries)
{
    public static MediumExportLimits From(MediumImportOptions o) =>
        new(o.MaxEntryBytes, o.MaxTotalBytes, o.MaxEntries);
}

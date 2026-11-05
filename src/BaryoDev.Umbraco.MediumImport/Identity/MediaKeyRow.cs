using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace BaryoDev.Umbraco.MediumImport.Identity;

[TableName(TableName)]
[PrimaryKey(nameof(SourceHash), AutoIncrement = false)]
[ExplicitColumns]
public sealed class MediaKeyRow
{
    public const string TableName = "mediumImportMedia";

    [Column("SourceHash")]
    [PrimaryKeyColumn(AutoIncrement = false)]
    [Length(64)]
    public string SourceHash { get; set; } = string.Empty;

    [Column("SourceUrl")]
    [Length(2048)]
    public string SourceUrl { get; set; } = string.Empty;

    [Column("MediaKey")]
    public Guid MediaKey { get; set; }
}

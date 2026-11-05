using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace BaryoDev.Umbraco.MediumImport.Identity;

[TableName(TableName)]
[PrimaryKey(nameof(PostId), AutoIncrement = false)]
[ExplicitColumns]
public sealed class StoryKeyRow
{
    public const string TableName = "mediumImportStory";

    [Column("PostId")]
    [PrimaryKeyColumn(AutoIncrement = false)]
    [Length(32)]
    public string PostId { get; set; } = string.Empty;

    [Column("ContentKey")]
    public Guid ContentKey { get; set; }

    [Column("ValuesHash")]
    [Length(64)]
    public string ValuesHash { get; set; } = string.Empty;

    [Column("ImportedUtc")]
    public DateTime ImportedUtc { get; set; }
}

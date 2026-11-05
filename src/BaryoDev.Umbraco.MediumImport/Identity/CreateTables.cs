using Umbraco.Cms.Infrastructure.Migrations;

namespace BaryoDev.Umbraco.MediumImport.Identity;

public sealed class CreateTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists(StoryKeyRow.TableName))
        {
            Create.Table<StoryKeyRow>().Do();
        }
        if (!TableExists(MediaKeyRow.TableName))
        {
            Create.Table<MediaKeyRow>().Do();
        }
        return Task.CompletedTask;
    }
}

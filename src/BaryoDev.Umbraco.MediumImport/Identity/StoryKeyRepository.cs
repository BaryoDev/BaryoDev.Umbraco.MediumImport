using System.Data.Common;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.MediumImport.Identity;

public sealed class StoryKeyRepository(IScopeProvider scopeProvider) : IStoryKeyRepository
{
    public StoryKeyRow? Get(string postId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.SingleOrDefault<StoryKeyRow>("WHERE PostId = @0", postId);
    }

    public void Upsert(StoryKeyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using var scope = scopeProvider.CreateScope();
        var existing = scope.Database.SingleOrDefault<StoryKeyRow>("WHERE PostId = @0", row.PostId);
        if (existing is null)
        {
            try
            {
                scope.Database.Insert(row);
            }
            catch (DbException)
            {
                // another writer inserted the same key first
                scope.Database.Update(row);
            }
        }
        else
        {
            scope.Database.Update(row);
        }
        scope.Complete();
    }
}

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

    public void Upsert(StoryKeyRow row) => UpsertCore(row, skipRead: false);

    /// <summary>Test seam: skipRead forces the insert path so the duplicate-key retry runs.</summary>
    internal void UpsertCore(StoryKeyRow row, bool skipRead)
    {
        ArgumentNullException.ThrowIfNull(row);
        using var scope = scopeProvider.CreateScope();
        var existing = skipRead ? null : scope.Database.SingleOrDefault<StoryKeyRow>("WHERE PostId = @0", row.PostId);
        if (existing is null)
        {
            try
            {
                scope.Database.Insert(row);
            }
            catch (DbException)
            {
                // The insert failed, most likely because another writer inserted the same key first,
                // so update instead. If that updates nothing, the original error is rethrown.
                if (scope.Database.Update(row) == 0)
                {
                    throw;
                }
            }
        }
        else
        {
            scope.Database.Update(row);
        }
        scope.Complete();
    }
}

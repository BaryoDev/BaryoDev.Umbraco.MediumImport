using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.MediumImport.Identity;

public sealed class MediaKeyRepository(IScopeProvider scopeProvider) : IMediaKeyRepository
{
    public Guid? Get(Uri source)
    {
        var hash = HashOf(source);
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var row = scope.Database.SingleOrDefault<MediaKeyRow>("WHERE SourceHash = @0", hash);
        return row?.MediaKey;
    }

    public void Upsert(Uri source, Guid mediaKey) => UpsertCore(source, mediaKey, skipRead: false);

    /// <summary>Test seam: skipRead forces the insert path so the duplicate-key retry runs.</summary>
    public void UpsertCore(Uri source, Guid mediaKey, bool skipRead)
    {
        var row = new MediaKeyRow { SourceHash = HashOf(source), SourceUrl = source.AbsoluteUri, MediaKey = mediaKey };
        using var scope = scopeProvider.CreateScope();
        var existing = skipRead ? null : scope.Database.SingleOrDefault<MediaKeyRow>("WHERE SourceHash = @0", row.SourceHash);
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

    internal static string HashOf(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri)));
    }
}

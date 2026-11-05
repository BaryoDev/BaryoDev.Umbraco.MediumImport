using BaryoDev.Umbraco.MediumImport.Uploads;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BaryoDev.Umbraco.MediumImport.Tests;

public sealed class UploadTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mi-upload-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, true);
        }
    }

    private UploadStore Store(long cap = 1024, TimeSpan? ttl = null) =>
        new(Path.Combine(_temp, "MediumImport"), Options.Create(new MediumImportOptions { MaxUploadBytes = cap, UploadTtl = ttl ?? TimeSpan.FromHours(1) }));

    [Fact]
    public async Task Upload_roundtrips_and_deletes()
    {
        UploadStore store = Store();
        byte[] bytes = [1, 2, 3, 4];

        Guid id = await store.SaveAsync(new MemoryStream(bytes), CancellationToken.None);

        using (Stream s = store.Open(id))
        {
            var ms = new MemoryStream();
            await s.CopyToAsync(ms, TestContext.Current.CancellationToken);
            Assert.Equal(bytes, ms.ToArray());
        }

        store.Delete(id);
        Assert.False(File.Exists(store.PathOf(id)));
        store.Delete(id);
    }

    [Fact]
    public async Task Upload_over_cap_throws_and_leaves_no_file()
    {
        UploadStore store = Store(cap: 1000);

        await Assert.ThrowsAsync<UploadTooLargeException>(() =>
            store.SaveAsync(new MemoryStream(new byte[300_000]), CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(store.Root));
    }

    [Fact]
    public async Task Upload_exactly_at_cap_is_kept()
    {
        UploadStore store = Store(cap: 1000);

        Guid id = await store.SaveAsync(new MemoryStream(new byte[1000]), CancellationToken.None);

        Assert.True(File.Exists(store.PathOf(id)));
    }

    [Fact]
    public async Task Upload_path_is_outside_wwwroot()
    {
        UploadStore store = Store();
        Guid id = await store.SaveAsync(new MemoryStream([1]), CancellationToken.None);

        string path = Path.GetFullPath(store.PathOf(id));

        Assert.StartsWith(Path.GetFullPath(_temp), path);
        Assert.DoesNotContain("wwwroot", path.Split(Path.DirectorySeparatorChar), StringComparer.OrdinalIgnoreCase);
        Assert.Matches(@"[0-9a-f]{32}\.zip$", Path.GetFileName(path));
    }

    [Fact]
    public async Task Cleanup_deletes_only_old_files()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
        UploadStore store = Store(ttl: TimeSpan.FromHours(1));
        Guid oldId = await store.SaveAsync(new MemoryStream([1]), CancellationToken.None);
        Guid freshId = await store.SaveAsync(new MemoryStream([2]), CancellationToken.None);
        File.SetLastWriteTimeUtc(store.PathOf(oldId), time.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(61));
        File.SetLastWriteTimeUtc(store.PathOf(freshId), time.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(59));
        var cleanup = new UploadCleanup(
            store, Options.Create(new MediumImportOptions { UploadTtl = TimeSpan.FromHours(1) }), time, NullLogger<UploadCleanup>.Instance);

        int deleted = cleanup.Sweep();

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(store.PathOf(oldId)));
        Assert.True(File.Exists(store.PathOf(freshId)));

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, cleanup.Sweep());
        Assert.Empty(Directory.EnumerateFiles(store.Root));
    }

    [Fact]
    public void Cleanup_with_no_folder_does_nothing()
    {
        var cleanup = new UploadCleanup(
            Store(), Options.Create(new MediumImportOptions()), new FakeTimeProvider(), NullLogger<UploadCleanup>.Instance);

        Assert.Equal(0, cleanup.Sweep());
    }
}

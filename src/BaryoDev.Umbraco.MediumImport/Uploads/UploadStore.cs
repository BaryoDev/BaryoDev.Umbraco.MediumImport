using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Hosting;

namespace BaryoDev.Umbraco.MediumImport.Uploads;

/// <summary>Keeps uploaded export zips in the local temp folder, never under wwwroot.</summary>
public sealed class UploadStore
{
    private readonly MediumImportOptions _options;

    public UploadStore(IHostingEnvironment hosting, IOptions<MediumImportOptions> options)
        : this(Path.Combine(hosting.LocalTempPath, "MediumImport"), options)
    {
    }

    internal UploadStore(string root, IOptions<MediumImportOptions> options)
    {
        Root = root;
        _options = options.Value;
    }

    public string Root { get; }

    public async Task<Guid> SaveAsync(Stream body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        Directory.CreateDirectory(Root);
        Guid id = Guid.NewGuid();
        string path = PathOf(id);
        long cap = _options.MaxUploadBytes;
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = await body.ReadAsync(buffer, ct)) > 0)
            {
                total += n;
                if (total > cap)
                {
                    throw new UploadTooLargeException(cap);
                }

                await file.WriteAsync(buffer.AsMemory(0, n), ct);
            }
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        return id;
    }

    public Stream Open(Guid id) =>
        new FileStream(PathOf(id), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public void Delete(Guid id) => TryDelete(PathOf(id));

    internal string PathOf(Guid id) => Path.Combine(Root, $"{id:N}.zip");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

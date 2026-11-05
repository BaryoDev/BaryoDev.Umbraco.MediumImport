using System.IO.Compression;
using System.Text;

namespace BaryoDev.Umbraco.MediumImport.Parsing;

/// <summary>
/// Reads the posts of a Medium export zip. The zip is untrusted: only posts/*.html entries are read, in memory,
/// and nothing is extracted to disk.
/// </summary>
public static class MediumExportReader
{
    public static IReadOnlyList<MediumStory> Read(Stream zip, MediumExportLimits limits)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new MediumExportException("The file is not a zip: " + ex.Message);
        }

        using (archive)
        {
            if (archive.Entries.Count > limits.MaxEntries)
            {
                throw new MediumExportException($"The zip has more than {limits.MaxEntries} entries.");
            }

            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                CheckName(entry.FullName);
                total += entry.Length;
                if (total > limits.MaxTotalBytes)
                {
                    throw new MediumExportException("The zip is too large.");
                }
            }

            var stories = new List<MediumStory>();
            long read = 0;
            int posts = 0;
            foreach (ZipArchiveEntry entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (!IsPost(entry.FullName))
                {
                    continue;
                }

                posts++;
                byte[] bytes = ReadCounted(entry, limits.MaxEntryBytes);
                read += bytes.Length;
                if (read > limits.MaxTotalBytes)
                {
                    throw new MediumExportException("The zip is too large.");
                }

                string fileName = entry.FullName[(entry.FullName.LastIndexOf('/') + 1)..];
                if (!MediumPostParser.HasPostId(fileName))
                {
                    continue;
                }

                stories.Add(MediumPostParser.Parse(fileName, Encoding.UTF8.GetString(bytes)));
            }

            if (posts == 0)
            {
                throw new MediumExportException("The zip has no posts/*.html entries.");
            }

            return stories;
        }
    }

    private static bool IsPost(string name) =>
        name.StartsWith("posts/", StringComparison.Ordinal)
        && name.IndexOf('/', "posts/".Length) < 0
        && name.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

    private static void CheckName(string name)
    {
        if (name.Length == 0 || name.StartsWith('/') || name.Contains('\\') || name.Contains(':')
            || name.Contains('\0') || name.Split('/').Any(p => p == ".."))
        {
            throw new MediumExportException($"Unsafe zip entry name: {name}");
        }
    }

    /// <summary>Counts the bytes actually read, so a header Length that lies cannot get past the cap.</summary>
    private static byte[] ReadCounted(ZipArchiveEntry entry, long cap)
    {
        using Stream s = entry.Open();
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (ms.Length + n > cap)
            {
                throw new MediumExportException($"{entry.FullName} is over {cap} bytes.");
            }

            ms.Write(buffer, 0, n);
        }

        return ms.ToArray();
    }
}

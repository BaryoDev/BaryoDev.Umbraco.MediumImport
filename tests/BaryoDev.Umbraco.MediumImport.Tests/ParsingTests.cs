using System.IO.Compression;
using System.Text;
using BaryoDev.Umbraco.MediumImport.Parsing;

namespace BaryoDev.Umbraco.MediumImport.Tests;

public class ParsingTests
{
    private static readonly MediumExportLimits Roomy = MediumExportLimits.From(new MediumImportOptions());

    private static string FixturePath(string prefix) =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "posts"), prefix + "*.html").Single();

    private static MediumStory ParseFixture(string prefix)
    {
        string path = FixturePath(prefix);
        return MediumPostParser.Parse(Path.GetFileName(path), File.ReadAllText(path));
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static (string, string) Post(string prefix) =>
        ("posts/" + Path.GetFileName(FixturePath(prefix)), File.ReadAllText(FixturePath(prefix)));

    [Fact]
    public void Parse_reads_id_title_summary_tags_date()
    {
        MediumStory s = ParseFixture("2024-01-02");

        Assert.Equal("0a1b2c3d4e5f", s.PostId);
        Assert.Equal("Code and gists", s.Title);
        Assert.Equal("Code blocks and gists survive the import.", s.Summary);
        Assert.Equal(["golang", "gist"], s.Tags);
        Assert.Equal(new DateTime(2024, 1, 2, 10, 0, 0, DateTimeKind.Utc), s.Published);
        Assert.Equal(new Uri("https://medium.com/@someone/code-and-gists-0a1b2c3d4e5f"), s.MediumUrl);
        Assert.Equal(3, s.Images.Count);
        Assert.False(s.IsDraft);
        Assert.False(s.IsResponse);
    }

    [Fact]
    public void Parse_removes_title_and_subtitle_from_body()
    {
        MediumStory s = ParseFixture("2024-01-02");

        Assert.Equal("Code blocks and gists survive the import.", s.Subtitle);
        Assert.DoesNotContain("graf--title", s.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("graf--subtitle", s.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("hljs-keyword", s.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_lists_gist_ids_once()
    {
        Assert.Equal(["0123456789abcdef"], ParseFixture("2024-01-02").GistIds);
    }

    [Fact]
    public void Draft_and_response_are_flagged()
    {
        Assert.True(ParseFixture("draft_").IsDraft);
        MediumStory response = ParseFixture("2024-04-10");
        Assert.True(response.IsResponse);
        Assert.False(response.IsDraft);
        Assert.False(ParseFixture("2025-01-15").IsResponse);
    }

    [Fact]
    public void Reader_opens_only_posts_html()
    {
        using MemoryStream zip = Zip(
            Post("2024-01-02"),
            ("profile/profile.html", "<html></html>"),
            ("posts/x.txt", "nope"));

        IReadOnlyList<MediumStory> stories = MediumExportReader.Read(zip, Roomy);

        Assert.Equal("0a1b2c3d4e5f", stories.Single().PostId);
    }

    [Fact]
    public void Reader_reads_the_ported_fixtures()
    {
        using MemoryStream zip = Zip(Post("2024-01-02"), Post("2024-03-02"), Post("2024-04-10"), Post("2025-01-15"), Post("draft_"));

        Assert.Equal(5, MediumExportReader.Read(zip, Roomy).Count);
    }

    [Fact]
    public void Reader_stops_at_the_entry_count_cap()
    {
        using MemoryStream zip = Zip(Post("2024-01-02"), Post("2024-03-02"), Post("2024-04-10"));

        Assert.Throws<MediumExportException>(() => MediumExportReader.Read(zip, Roomy with { MaxEntries = 2 }));
    }

    [Fact]
    public void Reader_stops_at_the_total_size_cap()
    {
        using MemoryStream zip = Zip(Post("2024-01-02"), Post("2024-03-02"));

        Assert.Throws<MediumExportException>(() => MediumExportReader.Read(zip, Roomy with { MaxTotalBytes = 100 }));
    }

    [Fact]
    public void Reader_stops_at_the_entry_size_cap()
    {
        using MemoryStream zip = Zip(Post("2024-01-02"));

        Assert.Throws<MediumExportException>(() => MediumExportReader.Read(zip, Roomy with { MaxEntryBytes = 100 }));
    }

    [Theory]
    [InlineData("posts/../../evil-0a1b2c3d4e5f.html")]
    [InlineData("/posts/evil-0a1b2c3d4e5f.html")]
    [InlineData("posts\\evil-0a1b2c3d4e5f.html")]
    public void Reader_rejects_path_traversal_names(string name)
    {
        using MemoryStream zip = Zip((name, "<html></html>"), Post("2024-01-02"));

        Assert.Throws<MediumExportException>(() => MediumExportReader.Read(zip, Roomy));
    }

    [Fact]
    public void Reader_throws_on_empty_export()
    {
        using MemoryStream zip = Zip(("profile/profile.html", "<html></html>"));

        Assert.Throws<MediumExportException>(() => MediumExportReader.Read(zip, Roomy));
    }

    [Fact]
    public void Reader_throws_on_a_non_zip()
    {
        using var junk = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip file at all"));

        Assert.Throws<MediumExportException>(() => MediumExportReader.Read(junk, Roomy));
    }

    [Fact]
    public void Reader_skips_files_without_a_post_id()
    {
        using MemoryStream zip = Zip(Post("2024-01-02"), ("posts/no-id.html", "<html></html>"));

        Assert.Single(MediumExportReader.Read(zip, Roomy));
    }

    [Fact]
    public void Reader_wraps_corrupt_entry_as_export_exception()
    {
        MemoryStream zip = Zip(Post("2024-01-02"));
        byte[] bytes = zip.ToArray();
        int nameLen = BitConverter.ToUInt16(bytes, 26);
        int extraLen = BitConverter.ToUInt16(bytes, 28);
        bytes[30 + nameLen + extraLen] = 0xFF;

        Assert.Throws<MediumExportException>(() => MediumExportReader.Read(new MemoryStream(bytes), Roomy));
    }

    [Fact]
    public void Reader_total_cap_counts_bytes_read()
    {
        long larger = Math.Max(new FileInfo(FixturePath("2024-01-02")).Length, new FileInfo(FixturePath("2024-03-02")).Length);
        using MemoryStream zip = Zip(Post("2024-01-02"), Post("2024-03-02"));

        Assert.Throws<MediumExportException>(() =>
            MediumExportReader.Read(zip, Roomy with { MaxEntryBytes = 1_000_000, MaxTotalBytes = larger + 10 }));
    }

    [Fact]
    public void Reader_buffers_a_non_seekable_stream_and_caps_it()
    {
        MemoryStream zip = Zip(Post("2024-01-02"));
        Assert.Single(MediumExportReader.Read(new ForwardOnly(zip.ToArray()), Roomy));
        Assert.Throws<MediumExportException>(() =>
            MediumExportReader.Read(new ForwardOnly(zip.ToArray()), Roomy with { MaxTotalBytes = 50 }));
    }

    private sealed class ForwardOnly(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    [Fact]
    public void Parse_falls_back_to_title_tag_and_reads_rel_tag_lists()
    {
        MediumStory s = MediumPostParser.Parse("a-0a1b2c3d4e5f.html",
            "<html><head><title>Fallback</title></head><body><h1 class=\"p-name\"> </h1>"
            + "<section data-field=\"body\"><p>x</p></section><a rel=\"tag nofollow\" href=\"/t\">t1</a></body></html>");

        Assert.Equal("Fallback", s.Title);
        Assert.Equal(["t1"], s.Tags);
    }
}

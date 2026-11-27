using System.Text.Json;
using System.Xml.Linq;

namespace BaryoDev.Umbraco.MediumImport.Tests;

/// <summary>The dashboard script URL carries the package version, so browsers fetch the new script after an upgrade.</summary>
public class PackageVersionTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BaryoDev.Umbraco.MediumImport.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Manifest_version_and_script_url_match_the_package_version()
    {
        var src = Path.Combine(Root(), "src", "BaryoDev.Umbraco.MediumImport");
        var version = XDocument.Load(Path.Combine(src, "BaryoDev.Umbraco.MediumImport.csproj"))
            .Descendants("Version").Single().Value;
        using var manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(src, "wwwroot", "App_Plugins", "BaryoDev.MediumImport", "umbraco-package.json")));

        Assert.Equal(version, manifest.RootElement.GetProperty("version").GetString());
        var elements = manifest.RootElement.GetProperty("extensions").EnumerateArray()
            .Where(ext => ext.TryGetProperty("element", out _))
            .Select(ext => ext.GetProperty("element").GetString())
            .ToList();
        Assert.NotEmpty(elements);
        Assert.All(elements, element => Assert.EndsWith($".js?v={version}", element));
    }

    private static string Script() => File.ReadAllText(Path.Combine(Root(), "src", "BaryoDev.Umbraco.MediumImport",
        "wwwroot", "App_Plugins", "BaryoDev.MediumImport", "medium-import-dashboard.js"));

    [Fact]
    public void Dashboard_loads_the_stylesheet_with_the_script_version_query()
    {
        var script = Script();

        Assert.Contains("const V = new URL(import.meta.url).search;", script);
        Assert.Contains("dashboard.css${V}", script);
    }

    [Fact]
    public void Dashboard_does_not_load_the_removed_brand_stylesheet()
    {
        Assert.DoesNotContain("brand.css", Script());
    }
}

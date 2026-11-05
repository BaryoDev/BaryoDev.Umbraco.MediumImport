using BaryoDev.Umbraco.MediumImport.Identity;

namespace BaryoDev.Umbraco.MediumImport.Tests;

public class ValuesHashTests
{
    [Fact]
    public void Order_does_not_matter()
    {
        var a = new Dictionary<string, string?> { ["title"] = "T", ["body"] = "B" };
        var b = new Dictionary<string, string?> { ["body"] = "B", ["title"] = "T" };
        Assert.Equal(ValuesHash.Compute(a), ValuesHash.Compute(b));
    }

    [Fact]
    public void Value_change_changes_hash()
    {
        var a = new Dictionary<string, string?> { ["title"] = "T" };
        var b = new Dictionary<string, string?> { ["title"] = "U" };
        Assert.NotEqual(ValuesHash.Compute(a), ValuesHash.Compute(b));
    }

    [Fact]
    public void Null_and_empty_are_equal()
    {
        var a = new Dictionary<string, string?> { ["title"] = null };
        var b = new Dictionary<string, string?> { ["title"] = "" };
        Assert.Equal(ValuesHash.Compute(a), ValuesHash.Compute(b));
    }
}

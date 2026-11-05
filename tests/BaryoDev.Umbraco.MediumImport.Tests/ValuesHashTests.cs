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

    [Fact]
    public void Date_is_the_stored_form_whatever_was_assigned()
    {
        var assigned = new DateTime(2025, 3, 31, 4, 28, 52, 566, DateTimeKind.Utc);
        var stored = new DateTime(2025, 3, 31, 4, 28, 52, DateTimeKind.Unspecified);
        Assert.Equal("2025-03-31T04:28:52.0000000", ValuesHash.Date(assigned));
        Assert.Equal(ValuesHash.Date(stored), ValuesHash.Date(assigned));
    }
}

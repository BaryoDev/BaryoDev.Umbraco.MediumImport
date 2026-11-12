namespace BaryoDev.Umbraco.MediumImport.Tests;

public class SmokeTests
{
    [Fact]
    public void Options_defaults_match_spec()
    {
        var options = new MediumImportOptions();

        Assert.Equal(104857600, options.MaxUploadBytes);
        Assert.Equal(104857600, options.MaxTotalBytes);
        Assert.Equal(TimeSpan.FromHours(1), options.UploadTtl);
    }
}

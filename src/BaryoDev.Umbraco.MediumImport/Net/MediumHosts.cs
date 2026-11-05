namespace BaryoDev.Umbraco.MediumImport.Net;

/// <summary>The hosts Medium serves post images from.</summary>
public static class MediumHosts
{
    public static bool IsMediumImage(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("cdn-images-1.medium.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("miro.medium.com", StringComparison.OrdinalIgnoreCase));
}

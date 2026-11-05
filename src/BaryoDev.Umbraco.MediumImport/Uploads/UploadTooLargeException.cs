namespace BaryoDev.Umbraco.MediumImport.Uploads;

public sealed class UploadTooLargeException(long limit) : Exception($"The upload is larger than {limit} bytes.")
{
    public long Limit { get; } = limit;
}

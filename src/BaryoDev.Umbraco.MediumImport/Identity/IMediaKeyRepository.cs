namespace BaryoDev.Umbraco.MediumImport.Identity;

public interface IMediaKeyRepository
{
    Guid? Get(Uri source);

    void Upsert(Uri source, Guid mediaKey);
}

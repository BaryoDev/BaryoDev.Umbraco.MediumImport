namespace BaryoDev.Umbraco.MediumImport.Identity;

public interface IStoryKeyRepository
{
    StoryKeyRow? Get(string postId);

    void Upsert(StoryKeyRow row);
}

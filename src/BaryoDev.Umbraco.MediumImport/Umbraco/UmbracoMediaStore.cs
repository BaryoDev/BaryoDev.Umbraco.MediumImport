using BaryoDev.Umbraco.MediumImport.Conversion;
using BaryoDev.Umbraco.MediumImport.Net;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace BaryoDev.Umbraco.MediumImport.Content;

/// <summary>Saves images under "Medium import", one subfolder per post.</summary>
public sealed class UmbracoMediaStore : IMediaStore
{
    public const string RootFolderName = "Medium import";

    private readonly IMediaService _media;
    private readonly MediaFileManager _files;
    private readonly MediaUrlGeneratorCollection _urlGenerators;
    private readonly IShortStringHelper _shortString;
    private readonly IContentTypeBaseServiceProvider _typeProvider;

    public UmbracoMediaStore(
        IMediaService media, MediaFileManager files, MediaUrlGeneratorCollection urlGenerators,
        IShortStringHelper shortString, IContentTypeBaseServiceProvider typeProvider)
    {
        _media = media;
        _files = files;
        _urlGenerators = urlGenerators;
        _shortString = shortString;
        _typeProvider = typeProvider;
    }

    public ResolvedImage Save(string postFolder, FetchedImage image, int userId)
    {
        IMedia root = FindRoot() ?? MakeFolder(RootFolderName, CoreConstants.System.Root, userId);
        IMedia folder = FindChild(root.Id, postFolder) ?? MakeFolder(postFolder, root.Id, userId);

        IMedia item = _media.CreateMedia(
            Path.GetFileNameWithoutExtension(image.FileName), folder.Id, CoreConstants.Conventions.MediaTypes.Image, userId);
        using var stream = new MemoryStream(image.Bytes);
        item.SetValue(_files, _urlGenerators, _shortString, _typeProvider, CoreConstants.Conventions.Media.File, image.FileName, stream);
        _media.Save(item, userId);

        string url = item.GetUrl(CoreConstants.Conventions.Media.File, _urlGenerators)
            ?? throw new InvalidOperationException($"No URL for {image.FileName}");
        return new ResolvedImage(url, item.Key);
    }

    public ResolvedImage? Find(Guid mediaKey)
    {
        IMedia? item = _media.GetById(mediaKey);
        string? url = item?.GetUrl(CoreConstants.Conventions.Media.File, _urlGenerators);
        return item is null || url is null ? null : new ResolvedImage(url, item.Key);
    }

    private IMedia? FindRoot() =>
        _media.GetRootMedia().FirstOrDefault(m => m.Name == RootFolderName && m.ContentType.Alias == CoreConstants.Conventions.MediaTypes.Folder);

    private IMedia? FindChild(int parentId, string name)
    {
        long page = 0;
        while (true)
        {
            IMedia[] children = _media.GetPagedChildren(parentId, page, 200, out long total).ToArray();
            IMedia? hit = children.FirstOrDefault(m => m.Name == name && m.ContentType.Alias == CoreConstants.Conventions.MediaTypes.Folder);
            if (hit is not null)
            {
                return hit;
            }

            page++;
            if (children.Length == 0 || page * 200 >= total)
            {
                return null;
            }
        }
    }

    private IMedia MakeFolder(string name, int parentId, int userId)
    {
        IMedia folder = _media.CreateMedia(name, parentId, CoreConstants.Conventions.MediaTypes.Folder, userId);
        _media.Save(folder, userId);
        return folder;
    }
}

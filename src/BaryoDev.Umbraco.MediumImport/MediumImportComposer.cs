using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace BaryoDev.Umbraco.MediumImport;

public sealed class MediumImportComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<MediumImportOptions>().BindConfiguration(MediumImportOptions.Section);
    }
}

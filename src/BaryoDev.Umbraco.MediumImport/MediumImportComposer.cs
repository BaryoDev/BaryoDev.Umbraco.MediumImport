using System.Net.Http.Headers;
using BaryoDev.Umbraco.MediumImport.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace BaryoDev.Umbraco.MediumImport;

public sealed class MediumImportComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<MediumImportOptions>().BindConfiguration(MediumImportOptions.Section);

        builder.Services.AddHttpClient(HttpImageFetcher.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient((sp, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<MediumImportOptions>>().Value.HttpTimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BaryoDev.Umbraco.MediumImport", null));
            });
        builder.Services.AddTransient<IImageFetcher, HttpImageFetcher>();
        builder.Services.AddTransient<IGistClient, GistClient>();
    }
}

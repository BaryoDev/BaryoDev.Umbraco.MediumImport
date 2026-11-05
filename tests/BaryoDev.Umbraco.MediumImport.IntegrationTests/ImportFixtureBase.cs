using BaryoDev.Umbraco.MediumImport.Content;
using BaryoDev.Umbraco.MediumImport.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace BaryoDev.Umbraco.MediumImport.IntegrationTests;

[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public abstract class ImportFixtureBase : UmbracoIntegrationTest
{
    protected static readonly FieldMapping FullMapping =
        new("title", "body", "publishDate", "summary", "cover", "tags", "canonical", "originalUrl");

    protected IContentService Content => GetRequiredService<IContentService>();

    protected IMediaService Media => GetRequiredService<IMediaService>();

    protected Guid BlogKey { get; private set; }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        builder.Services.TryAddTransient<IPostWriter, UmbracoPostWriter>();
        builder.Services.TryAddTransient<IMediaStore, UmbracoMediaStore>();
        builder.Services.TryAddTransient<MappingOptions>();
    }

    [SetUp]
    public void SeedTypes() => BlogKey = TypeSeeder.Seed(Services);

    protected ImportSettings Settings(string docType = "post", bool publish = false, FieldMapping? mapping = null) =>
        new(BlogKey, docType, mapping ?? FullMapping, publish);
}

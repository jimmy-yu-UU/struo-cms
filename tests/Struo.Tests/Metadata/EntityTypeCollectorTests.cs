using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Metadata;
using Struo.Tests.Fixtures;
using Xunit;

namespace Struo.Tests.Metadata;

public sealed class EntityTypeCollectorTests
{
    private static ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddStruoMetadata(typeof(FxArticle).Assembly)
            .BuildServiceProvider();

    private static EntityTypeCollector Collector(IServiceProvider sp, IM2MDescriptorSource? m2m = null) =>
        new(
            sp.GetRequiredService<IMetadataProvider>(),
            sp.GetRequiredService<IEntityRegistry>(),
            m2m ?? sp.GetRequiredService<IM2MDescriptorSource>());

    [Fact]
    public void Collects_content_collections_sidecars_and_framework_builtins()
    {
        using var sp = BuildProvider();

        var types = Collector(sp).CollectForInitTables();

        types.Should().Contain(new[]
        {
            typeof(FxArticle), typeof(FxArticleTranslation), typeof(FxCategory),
            typeof(Struo.Infrastructure.Localization.Language),
            typeof(Struo.Infrastructure.Files.File),
            typeof(Struo.Infrastructure.Files.FileTranslation),
            typeof(Struo.Infrastructure.Identity.User),
            typeof(Struo.Infrastructure.Identity.Role),
            typeof(Struo.Infrastructure.Identity.Permission),
            typeof(Struo.Infrastructure.Identity.UserRole),
        });
    }

    [Fact]
    public void Result_is_deduplicated()
    {
        using var sp = BuildProvider();

        var types = Collector(sp).CollectForInitTables();

        types.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Includes_m2m_junction_types()
    {
        using var sp = BuildProvider();
        var fakeM2M = new FakeM2MSource();

        var types = Collector(sp, fakeM2M).CollectForInitTables();

        types.Should().Contain(typeof(FakeJunction));
    }

    private sealed class FakeJunction { }

    private sealed class FakeM2MSource : IM2MDescriptorSource
    {
        public IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection) =>
            collection == "fxArticle"
                ? new[] { new M2MDescriptor("tags", "fxTag", typeof(FakeJunction), "articleId", "tagId", null) }
                : Array.Empty<M2MDescriptor>();
    }
}

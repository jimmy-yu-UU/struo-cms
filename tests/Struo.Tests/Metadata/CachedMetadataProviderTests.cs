using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Metadata;

public class CachedMetadataProviderTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddStruoMetadata(typeof(Article).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_exposes_scanned_collections()
    {
        using var sp = Build();
        var provider = sp.GetRequiredService<IMetadataProvider>();
        provider.GetCollections().Select(c => c.Name)
            .Should().Contain(["article", "tag"]);
    }

    [Fact]
    public void GetCollection_is_case_insensitive_and_null_safe()
    {
        using var sp = Build();
        var provider = sp.GetRequiredService<IMetadataProvider>();
        provider.GetCollection("ARTICLE").Should().NotBeNull();
        provider.GetCollection("nope").Should().BeNull();
    }

    [Fact]
    public void Registry_is_cached_singleton_returning_same_instances()
    {
        using var sp = Build();
        var a = sp.GetRequiredService<IMetadataProvider>();
        var b = sp.GetRequiredService<IMetadataProvider>();
        a.Should().BeSameAs(b);                                   // singleton, scanned once
        a.GetCollection("article").Should().BeSameAs(b.GetCollection("article")); // cached instance
    }
}

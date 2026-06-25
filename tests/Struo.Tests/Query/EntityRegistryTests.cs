using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Query;

public class EntityRegistryTests
{
    private static IEntityRegistry Build()
    {
        var services = new ServiceCollection();
        services.AddStruoMetadata(typeof(Article).Assembly);
        return services.BuildServiceProvider().GetRequiredService<IEntityRegistry>();
    }

    [Fact]
    public void Resolves_entity_type_and_id_for_collection()
    {
        var d = Build().Get("article");
        d.Should().NotBeNull();
        d!.EntityType.Should().Be(typeof(Article));
        d.IdProperty.Should().Be("Id");
    }

    [Fact]
    public void Maps_camel_field_names_to_clr_properties()
    {
        var d = Build().Get("article")!;
        d.FieldToProperty["title"].Should().Be("Title");
        d.FieldToProperty["seoTitle"].Should().Be("SeoTitle");
        d.FieldToProperty["createdAt"].Should().Be("CreatedAt");
    }

    [Fact]
    public void Get_is_case_insensitive_and_null_for_unknown()
    {
        var reg = Build();
        reg.Get("ARTICLE").Should().NotBeNull();
        reg.Get("nope").Should().BeNull();
    }
}

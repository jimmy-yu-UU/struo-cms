using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The NOT NULL properties the write path refuses a null for are the columns SqlSugar applies as NOT NULL,
/// for every registered collection entity, translation sidecar and junction payload.
/// </summary>
[Collection("ApiIntegration")]
public sealed class NotNullRuleMatchesColumnsTests(ApiFactory factory)
{
    private static HashSet<string> AppliedNotNull(ISqlSugarClient db, Type entityType) =>
        db.EntityMaintenance.GetEntityInfo(entityType).Columns
            .Where(c => !c.IsIgnore && !c.IsPrimarykey && !c.IsNullable)
            .Where(c => c.PropertyInfo is { CanWrite: true } p && !p.PropertyType.IsValueType)
            .Select(c => c.PropertyName)
            .ToHashSet(StringComparer.Ordinal);

    private static (IServiceScope Scope, ISqlSugarClient Db, IMetadataProvider Metadata, IEntityRegistry Registry,
        IM2MDescriptorSource Relations) Resolve(IServiceProvider services)
    {
        var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        return (scope, provider.GetRequiredService<ISqlSugarClient>(), provider.GetRequiredService<IMetadataProvider>(),
            provider.GetRequiredService<IEntityRegistry>(), provider.GetRequiredService<IM2MDescriptorSource>());
    }

    [Fact]
    public void Every_collection_entity_refuses_null_for_the_columns_SqlSugar_applies_as_NOT_NULL()
    {
        var (scope, db, metadata, registry, _) = Resolve(factory.Services);
        using var _s = scope;
        var collections = metadata.GetCollections();

        foreach (var collection in collections)
        {
            var descriptor = registry.Get(collection.Name)!;
            descriptor.NotNullProperties.Should().BeEquivalentTo(
                AppliedNotNull(db, descriptor.EntityType), $"collection '{collection.Name}'");
        }

        collections.Select(c => registry.Get(c.Name)!.NotNullProperties.Count).Sum().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Every_translation_sidecar_refuses_null_for_the_columns_SqlSugar_applies_as_NOT_NULL()
    {
        var (scope, db, metadata, _, _) = Resolve(factory.Services);
        using var _s = scope;
        var sidecars = metadata.GetCollections().Where(c => c.Translation is not null).ToList();

        foreach (var collection in sidecars)
        {
            var translation = collection.Translation!;
            var applied = AppliedNotNull(db, translation.TranslationEntityType);
            var expected = translation.Fields
                .Select(f => translation.TranslationEntityType.GetProperty(
                    f, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                       | System.Reflection.BindingFlags.IgnoreCase)!)
                .Where(p => applied.Contains(p.Name))
                .Select(p => p.Name);
            translation.NotNullFields.Keys
                .Select(k => translation.TranslationEntityType.GetProperty(
                    k, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                       | System.Reflection.BindingFlags.IgnoreCase)!.Name)
                .Should().BeEquivalentTo(expected, $"sidecar of '{collection.Name}'");
        }

        sidecars.Sum(c => c.Translation!.NotNullFields.Count).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Every_junction_payload_refuses_null_for_the_columns_SqlSugar_applies_as_NOT_NULL()
    {
        var (scope, db, metadata, _, relations) = Resolve(factory.Services);
        using var _s = scope;
        var descriptors = metadata.GetCollections()
            .SelectMany(c => relations.M2MDescriptors(c.Name))
            .Where(d => d.HasPayload)
            .ToList();

        foreach (var descriptor in descriptors)
        {
            var applied = AppliedNotNull(db, descriptor.JunctionType);
            foreach (var field in descriptor.JunctionPayload!)
                field.NotNull.Should().Be(
                    applied.Contains(field.Property), $"{descriptor.JunctionType.Name}.{field.Property}");
        }

        descriptors.SelectMany(d => d.JunctionPayload!).Count(f => f.NotNull).Should().BeGreaterThan(0);
    }
}

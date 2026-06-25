using System.Reflection;
using System.Text.Json;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Seo;

namespace Struo.Infrastructure.Metadata;

public static class MetadataScanner
{
    private static readonly HashSet<FieldInterface> OptionInterfaces =
    [
        FieldInterface.Select, FieldInterface.MultiSelect, FieldInterface.Radio,
        FieldInterface.CheckboxGroup, FieldInterface.Tags
    ];

    private static readonly string[] AuditFieldNames =
        [nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy),
         nameof(IAuditable.UpdatedAt), nameof(IAuditable.UpdatedBy)];

    public static IReadOnlyList<CollectionMetadata> Scan(params Assembly[] assemblies) =>
        ScanTypes(assemblies.SelectMany(a => a.GetTypes()));

    public static IReadOnlyList<CollectionMetadata> ScanTypes(IEnumerable<Type> types)
    {
        var collections = new List<CollectionMetadata>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in types)
        {
            var collectionAttr = type.GetCustomAttribute<CmsCollectionAttribute>();
            if (collectionAttr is null) continue;

            var meta = BuildCollection(type, collectionAttr);
            if (!seen.Add(meta.Name))
                throw new MetadataException(
                    $"Duplicate collection name '{meta.Name}' (type '{type.FullName}').");
            collections.Add(meta);
        }

        return collections;
    }

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static CollectionMetadata BuildCollection(Type type, CmsCollectionAttribute attr)
    {
        var groups = type.GetCustomAttributes<CmsFieldGroupAttribute>()
            .Select(g => new FieldGroupMetadata(g.Name, g.Label, g.Sort))
            .ToList();

        var isAuditable = typeof(IAuditable).IsAssignableFrom(type);
        var fields = new List<(int order, FieldMetadata field)>();
        var order = 0;
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            order++;
            var fieldAttr = prop.GetCustomAttribute<CmsFieldAttribute>();
            var isAudit = isAuditable && AuditFieldNames.Contains(prop.Name);

            if (fieldAttr is not null)
                fields.Add((order, BuildField(prop, fieldAttr)));
            else if (isAudit)
                fields.Add((order, BuildSystemField(prop)));
            // properties with neither [CmsField] nor audit convention are ignored
        }

        // ISeoMeta convention
        if (typeof(ISeoMeta).IsAssignableFrom(type))
        {
            foreach (var seo in BuildSeoFields())
                fields.Add((++order, seo));
            if (groups.All(g => g.Name != "SEO"))
                groups.Add(new FieldGroupMetadata("SEO", "SEO", groups.Count + 1));
        }

        var ordered = fields
            .OrderBy(f => f.field.Sort)
            .ThenBy(f => f.order)
            .Select(f => f.field)
            .ToList();

        var name = Camel(attr.Name);
        var defaultDisplay = attr.DefaultDisplayField is null ? null : Camel(attr.DefaultDisplayField);
        if (defaultDisplay is not null && ordered.All(f => f.Name != defaultDisplay))
            throw new MetadataException(
                $"Collection '{name}' DefaultDisplayField '{attr.DefaultDisplayField}' is not a known field.");

        return new CollectionMetadata
        {
            Name = name,
            Label = attr.Label ?? attr.Name,
            Icon = attr.Icon,
            Group = attr.Group,
            DefaultDisplayField = defaultDisplay,
            FieldGroups = groups,
            Fields = ordered
        };
    }

    private static FieldMetadata BuildField(PropertyInfo prop, CmsFieldAttribute attr)
    {
        IReadOnlyList<FieldOption>? options = null;
        var optionsAttr = prop.GetCustomAttribute<CmsOptionsAttribute>();
        if (optionsAttr is not null)
        {
            if (!OptionInterfaces.Contains(attr.Interface))
                throw new MetadataException(
                    $"Field '{prop.DeclaringType?.Name}.{prop.Name}' has [CmsOptions] " +
                    $"but interface '{attr.Interface}' is not an option type.");
            options = ParseOptions(prop, optionsAttr);
        }

        return new FieldMetadata
        {
            Name = Camel(prop.Name),
            Label = attr.Label ?? prop.Name,
            Interface = attr.Interface,
            Required = attr.Required,
            Searchable = attr.Searchable,
            Sortable = attr.Sortable,
            ReadOnly = attr.ReadOnly,
            Hidden = attr.Hidden,
            Translatable = attr.Translatable,
            Sort = attr.Sort,
            HelpText = attr.HelpText,
            Group = attr.Group,
            Options = options,
            IsSystem = false
        };
    }

    private static IReadOnlyList<FieldOption> ParseOptions(PropertyInfo prop, CmsOptionsAttribute attr)
    {
        var list = new List<FieldOption>();
        foreach (var entry in attr.Options)
        {
            var idx = entry.IndexOf(':');
            if (idx <= 0 || idx == entry.Length - 1)
                throw new MetadataException(
                    $"Field '{prop.DeclaringType?.Name}.{prop.Name}' has malformed [CmsOptions] entry '{entry}' (expected 'value:label').");
            list.Add(new FieldOption(entry[..idx], entry[(idx + 1)..]));
        }
        return list;
    }

    private static FieldMetadata BuildSystemField(PropertyInfo prop) => new()
    {
        Name = Camel(prop.Name),
        Label = prop.Name,
        Interface = prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?)
            ? FieldInterface.DateTime
            : FieldInterface.Text,
        ReadOnly = true,
        IsSystem = true,
        Hidden = false,
        Sort = 1000
    };

    private static IEnumerable<FieldMetadata> BuildSeoFields()
    {
        yield return new FieldMetadata
        {
            Name = "seoTitle", Label = "SEO Title", Interface = FieldInterface.Text,
            Group = "SEO", Sort = 900
        };
        yield return new FieldMetadata
        {
            Name = "seoMetaDescription", Label = "SEO Meta Description", Interface = FieldInterface.Textarea,
            Group = "SEO", Sort = 901
        };
        yield return new FieldMetadata
        {
            Name = "seoOgImageId", Label = "OG Image", Interface = FieldInterface.Number,
            Group = "SEO", Sort = 902
        };
    }
}

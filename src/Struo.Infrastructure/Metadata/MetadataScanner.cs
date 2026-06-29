using System.Collections;
using System.Reflection;
using System.Text.Json;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
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

    public static IReadOnlyDictionary<string, EntityDescriptor> ScanDescriptors(IEnumerable<Type> types)
    {
        var map = new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (type.GetCustomAttribute<CmsCollectionAttribute>() is null) continue;

            var collection = Camel(type.Name);
            var fieldToProp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var idProperty = "Id";
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                fieldToProp[Camel(prop.Name)] = prop.Name;
                if (prop.GetCustomAttribute<SugarColumn>() is { IsPrimaryKey: true })
                    idProperty = prop.Name;
            }
            map[collection] = new EntityDescriptor(type, fieldToProp, idProperty);
        }
        return map;
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

        var (translation, translatableFields) = ScanTranslations(type);
        foreach (var tf in translatableFields) fields.Add((++order, tf));

        var ordered = fields
            .OrderBy(f => f.field.Sort)
            .ThenBy(f => f.order)
            .Select(f => f.field)
            .ToList();

        var name = Camel(type.Name);
        var defaultDisplay = attr.DefaultDisplayField is null ? null : Camel(attr.DefaultDisplayField);
        if (defaultDisplay is not null && ordered.All(f => f.Name != defaultDisplay))
            throw new MetadataException(
                $"Collection '{name}' DefaultDisplayField '{attr.DefaultDisplayField}' is not a known field.");

        return new CollectionMetadata
        {
            Name = name,
            Label = attr.Label,
            Icon = attr.Icon,
            Group = attr.Group,
            DefaultDisplayField = defaultDisplay,
            FieldGroups = groups,
            Fields = ordered,
            Relations = ScanRelations(type),
            Translation = translation
        };
    }

    /// <summary>
    /// Scans <paramref name="type"/> for a <c>[CmsTranslations(typeof(T))]</c> property. When present,
    /// validates the sidecar translation entity (Id PK, <c>{Parent}Id</c> FK, string <c>Locale</c>,
    /// at least one <c>[CmsField]</c>) and returns its <see cref="TranslationMetadata"/> together with
    /// the translatable fields (built from T's <c>[CmsField]</c>s, marked <c>Translatable=true</c>)
    /// to be merged into the parent collection's field list. Throws <see cref="MetadataException"/>
    /// on any violation (fail-fast at scan).
    /// </summary>
    private static (TranslationMetadata? meta, List<FieldMetadata> translatableFields) ScanTranslations(Type type)
    {
        var prop = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<CmsTranslationsAttribute>() is not null);
        if (prop is null) return (null, []);

        var tType = prop.GetCustomAttribute<CmsTranslationsAttribute>()!.TranslationEntityType;
        var tProps = tType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var hasId = tProps.Any(p => string.Equals(p.Name, "Id", StringComparison.Ordinal));
        var fkName = type.Name + "Id";   // convention {Parent}Id
        var fkProp = tProps.FirstOrDefault(p => string.Equals(p.Name, fkName, StringComparison.Ordinal));
        var localeProp = tProps.FirstOrDefault(p =>
            string.Equals(p.Name, "Locale", StringComparison.Ordinal) && p.PropertyType == typeof(string));
        var fieldProps = tProps.Where(p => p.GetCustomAttribute<CmsFieldAttribute>() is not null).ToList();

        if (!hasId)
            throw new MetadataException($"Translation entity '{tType.Name}' must have an 'Id' primary key.");
        if (fkProp is null)
            throw new MetadataException($"Translation entity '{tType.Name}' must have foreign key '{fkName}'.");
        if (localeProp is null)
            throw new MetadataException($"Translation entity '{tType.Name}' must have a string 'Locale' property.");
        if (fieldProps.Count == 0)
            throw new MetadataException($"Translation entity '{tType.Name}' must declare at least one [CmsField].");

        var fields = fieldProps
            .Select(p => BuildField(p, p.GetCustomAttribute<CmsFieldAttribute>()!) with { Translatable = true })
            .ToList();

        var meta = new TranslationMetadata
        {
            TranslationEntityType = tType,
            ForeignKeyProperty = fkName,
            LocaleProperty = "Locale",
            Fields = fields.Select(f => f.Name).ToList()
        };
        return (meta, fields);
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

    // ── Relation scanning ────────────────────────────────────────────────────

    /// <summary>
    /// Scans all properties on <paramref name="type"/> that carry both
    /// <c>[Navigate]</c> and <c>[CmsRelation]</c> and returns a
    /// <see cref="RelationMetadata"/> for each.
    /// </summary>
    /// <remarks>
    /// SqlSugar's <c>Navigate</c> attribute exposes no public properties —
    /// all data lives in constructor arguments, read via
    /// <see cref="CustomAttributeData"/>.
    /// Constructors:
    ///   M2O/O2M → <c>(NavigateType, string fk, string[]?)</c>
    ///   M2M     → <c>(Type junctionType, string parentFk, string targetFk, string[]?)</c>
    /// </remarks>
    public static IReadOnlyList<RelationMetadata> ScanRelations(Type type)
    {
        var list = new List<RelationMetadata>();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Both [Navigate] and [CmsRelation] required
            var navData = prop.CustomAttributes
                .FirstOrDefault(a => a.AttributeType == typeof(Navigate));
            var rel = prop.GetCustomAttribute<CmsRelationAttribute>();
            if (navData is null || rel is null) continue;

            var isCollection = prop.PropertyType.IsGenericType
                && typeof(IEnumerable).IsAssignableFrom(prop.PropertyType);

            Type target;
            RelationKind kind;
            string? fk = null;

            if (isCollection)
            {
                target = prop.PropertyType.GetGenericArguments()[0];
                kind = NavigateHasMappingType(navData)
                    ? RelationKind.ManyToMany
                    : RelationKind.OneToMany;
            }
            else
            {
                target = prop.PropertyType.IsGenericType
                    ? prop.PropertyType.GetGenericArguments()[0]
                    : prop.PropertyType;
                // Unwrap Nullable<T>
                target = Nullable.GetUnderlyingType(target) ?? target;
                kind = RelationKind.ManyToOne;
                fk = Camel(NavigateForeignKeyName(navData));
            }

            list.Add(new RelationMetadata
            {
                Name = Camel(prop.Name),
                Label = prop.Name,
                Kind = kind,
                TargetCollection = Camel(target.Name),
                Interface = rel.Interface,
                ForeignKey = fk,
                DisplayTemplate = rel.DisplayTemplate,
                PickerQuery = rel.PickerQuery,
                OnDelete = rel.OnDelete,
                Editable = rel.Editable,
                SelfReferencing = target == type
            });
        }
        return list;
    }

    // Navigate attribute has no public properties; read ctor args via CustomAttributeData.
    // M2M ctor: (Type MappingTableType, string typeAId, string typeBId, string[]?)
    // M2O/O2M ctor: (NavigateType, string fk, string[]?)

    /// <summary>Returns true when the Navigate ctor's first arg is a <see cref="Type"/> (M2M junction).</summary>
    internal static bool NavigateHasMappingType(CustomAttributeData navData) =>
        navData.ConstructorArguments.Count >= 1
        && navData.ConstructorArguments[0].ArgumentType == typeof(Type);

    /// <summary>Returns the junction mapping <see cref="Type"/> (M2M ctor arg 0).</summary>
    internal static Type? NavigateMappingType(CustomAttributeData navData) =>
        NavigateHasMappingType(navData)
            ? (Type?)navData.ConstructorArguments[0].Value
            : null;

    /// <summary>Returns the parent-side junction FK property name (M2M ctor arg 1).</summary>
    internal static string? NavigateMappingA(CustomAttributeData navData) =>
        NavigateHasMappingType(navData) && navData.ConstructorArguments.Count >= 2
            ? navData.ConstructorArguments[1].Value as string
            : null;

    /// <summary>Returns the target-side junction FK property name (M2M ctor arg 2).</summary>
    internal static string? NavigateMappingB(CustomAttributeData navData) =>
        NavigateHasMappingType(navData) && navData.ConstructorArguments.Count >= 3
            ? navData.ConstructorArguments[2].Value as string
            : null;

    /// <summary>Returns the FK property name for M2O/O2M (Navigate ctor arg 1).</summary>
    internal static string NavigateForeignKeyName(CustomAttributeData navData) =>
        navData.ConstructorArguments.Count >= 2
            ? (navData.ConstructorArguments[1].Value as string ?? string.Empty)
            : string.Empty;
}

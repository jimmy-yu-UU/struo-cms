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

    // 7g.5: interfaces whose undeclared MaxLength defaults to 255 (SqlSugar's default varchar width).
    // Content-bearing interfaces (RichText/Textarea/Markdown/Code/Json) default to unlimited.
    private static readonly HashSet<FieldInterface> ShortStringInterfaces =
    [
        FieldInterface.Text, FieldInterface.Slug, FieldInterface.Email, FieldInterface.Url,
        FieldInterface.Password, FieldInterface.Color, FieldInterface.Phone,
        FieldInterface.Select, FieldInterface.MultiSelect, FieldInterface.Radio,
        FieldInterface.CheckboxGroup, FieldInterface.Tags
    ];

    // 7g+ slice 3: JSON-column interfaces that live on the parent entity and cannot be translatable
    // (translating a structured aggregate is out of scope). A [CmsField(Translatable=true)] on any of
    // these fail-fasts at scan. Json is intentionally excluded (a string that could be translatable
    // in a later slice; non-translatable by convention here, but not fail-fasted).
    private static readonly HashSet<FieldInterface> NonTranslatableJsonInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags,
        FieldInterface.KeyValue, FieldInterface.Files, FieldInterface.Repeater
    ];

    // 7g+ slice 4: sub-field interfaces allowed inside a Repeater child object (the lean scalar set).
    // Everything else — RichText, File/Image/Files, multi-value, Json/KeyValue, nested Repeater,
    // Password/Hidden/Uuid/Divider — fail-fasts at scan.
    private static readonly HashSet<FieldInterface> RepeaterAllowedInterfaces =
    [
        FieldInterface.Text, FieldInterface.Textarea, FieldInterface.Markdown, FieldInterface.Code,
        FieldInterface.Slug, FieldInterface.Email, FieldInterface.Url, FieldInterface.Color,
        FieldInterface.Phone,
        FieldInterface.Number, FieldInterface.Slider, FieldInterface.Rating,
        FieldInterface.Boolean, FieldInterface.Checkbox,
        FieldInterface.Date, FieldInterface.Time, FieldInterface.DateTime,
        FieldInterface.Select, FieldInterface.Radio
    ];

    private static readonly string[] AuditFieldNames =
        [nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy),
         nameof(IAuditable.UpdatedAt), nameof(IAuditable.UpdatedBy)];

    public static IReadOnlyList<CollectionMetadata> Scan(params Assembly[] assemblies) =>
        ScanTypes(assemblies.SelectMany(SafeGetTypes));

    /// <summary>
    /// <see cref="Assembly.GetTypes"/> that tolerates an assembly with an unresolvable type: instead of
    /// throwing <see cref="ReflectionTypeLoadException"/> (which aborts the whole scan), it returns the
    /// types that did load. Guards convention-based discovery against a single bad dependency.
    /// </summary>
    public static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

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

            // Also map each declared many-to-one relation's local FK (camelCase -> CLR
            // property name) so the WHERE/sort column resolver (ConditionalModelTranslator.Column)
            // can resolve filters like "categoryId" even though the FK carries no [CmsField]
            // (it must NOT be added to meta.Fields — not projected/writable/required).
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var navData = prop.CustomAttributes
                    .FirstOrDefault(a => a.AttributeType == typeof(Navigate));
                var rel = prop.GetCustomAttribute<CmsRelationAttribute>();
                if (navData is null || rel is null) continue;

                var isCollection = prop.PropertyType.IsGenericType
                    && typeof(IEnumerable).IsAssignableFrom(prop.PropertyType);
                if (isCollection) continue; // only many-to-one relations have a local FK

                var fkProp = NavigateForeignKeyName(navData);
                if (!string.IsNullOrEmpty(fkProp))
                    fieldToProp[Camel(fkProp)] = fkProp;
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

        foreach (var (_, field) in fields)
            if (field.Translatable && NonTranslatableJsonInterfaces.Contains(field.Interface))
                throw new MetadataException(
                    $"Field '{field.Name}' uses interface '{field.Interface}', which cannot be translatable.");

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
            AdminOnly = attr.AdminOnly,
            Hidden = attr.Hidden,
            Revisions = attr.Revisions,
            SoftDelete = typeof(Struo.Domain.Auditing.ISoftDeletable).IsAssignableFrom(type),
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

        if (attr.MaxLength < 0)
            throw new MetadataException(
                $"Field '{prop.DeclaringType?.Name}.{prop.Name}' has a MaxLength that is negative.");
        if (attr.MaxLength > 0 && prop.PropertyType != typeof(string))
            throw new MetadataException(
                $"Field '{prop.DeclaringType?.Name}.{prop.Name}' declares MaxLength but is not a string property.");

        int? maxLength = attr.MaxLength > 0
            ? attr.MaxLength
            : prop.PropertyType == typeof(string) && ShortStringInterfaces.Contains(attr.Interface)
                ? 255
                : null;

        IReadOnlyList<FieldMetadata>? childFields = null;
        if (attr.Interface == FieldInterface.Repeater)
            childFields = BuildRepeaterChildFields(prop);

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
            MaxLength = maxLength,
            Options = options,
            Fields = childFields,
            IsSystem = false
        };
    }

    /// <summary>
    /// Resolves a Repeater's child-object sub-field schema by recursing into the element type of its
    /// <c>List&lt;TChild&gt;</c> property. Each sub-property carrying <c>[CmsField]</c> is scanned with
    /// the same <see cref="BuildField"/> logic; the sub-field interface must be in
    /// <see cref="RepeaterAllowedInterfaces"/> and must not be translatable. Fails fast on any
    /// violation of these guards.
    /// </summary>
    private static IReadOnlyList<FieldMetadata> BuildRepeaterChildFields(PropertyInfo prop)
    {
        var t = prop.PropertyType;
        var isList = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);
        var childType = isList ? t.GetGenericArguments()[0] : null;
        if (childType is null || !childType.IsClass || childType == typeof(string))
            throw new MetadataException(
                $"Repeater field '{Camel(prop.Name)}' must be a List<T> of a child object type.");

        var subProps = childType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<CmsFieldAttribute>() is not null)
            .ToList();
        if (subProps.Count == 0)
            throw new MetadataException(
                $"Repeater field '{Camel(prop.Name)}' child type '{childType.Name}' must declare at least one [CmsField].");

        var fields = new List<FieldMetadata>();
        foreach (var sub in subProps)
        {
            var subAttr = sub.GetCustomAttribute<CmsFieldAttribute>()!;
            if (!RepeaterAllowedInterfaces.Contains(subAttr.Interface))
                throw new MetadataException(
                    $"Repeater field '{Camel(prop.Name)}' sub-field '{Camel(sub.Name)}' uses interface " +
                    $"'{subAttr.Interface}', which is not allowed inside a Repeater.");
            if (subAttr.Translatable)
                throw new MetadataException(
                    $"Repeater field '{Camel(prop.Name)}' sub-field '{Camel(sub.Name)}' cannot be translatable.");
            fields.Add(BuildField(sub, subAttr));
        }
        return fields;
    }

    private static IReadOnlyList<FieldOption> ParseOptions(PropertyInfo prop, CmsOptionsAttribute attr)
    {
        var list = new List<FieldOption>();
        foreach (var entry in attr.Options)
        {
            var idx = entry.IndexOf(':');
            var value = idx < 0 ? entry : entry[..idx];
            if (string.IsNullOrWhiteSpace(value))
                throw new MetadataException(
                    $"Field '{prop.DeclaringType?.Name}.{prop.Name}' has malformed [CmsOptions] entry '{entry}' (value is required).");
            if (idx < 0)
            {
                // No colon: label defaults to the value ("draft" => value=label="draft").
                list.Add(new FieldOption(entry, entry));
                continue;
            }
            var label = entry[(idx + 1)..];
            list.Add(new FieldOption(value, string.IsNullOrWhiteSpace(label) ? value : label));
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
                if (kind == RelationKind.OneToMany)
                    // Reverse FK on the child entity (mirrors RelationshipGraph's
                    // ReverseForeignKeyProperty) so the frontend RelatedList knows
                    // which column to filter the child collection by.
                    fk = Camel(NavigateForeignKeyName(navData));
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

using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    // Sub-field interfaces allowed inside a Repeater child object (the lean scalar set).
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
        ScanTypes(assemblies.SelectMany(LoadTypes));

    /// <summary>
    /// Every type of <paramref name="assembly"/>. A type that cannot load (a missing or mismatched
    /// dependency) throws a <see cref="MetadataException"/> naming the assembly and the loader errors,
    /// because skipping the type would drop its collection from the API without a trace.
    /// </summary>
    public static IEnumerable<Type> LoadTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            throw AssemblyLoadFailure.ForAssembly(assembly, ex);
        }
    }

    public static IReadOnlyList<CollectionMetadata> ScanTypes(IEnumerable<Type> types)
    {
        var typeList = types as IReadOnlyList<Type> ?? types.ToList();
        var collections = new List<CollectionMetadata>();
        var seen = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in typeList)
        {
            var collectionAttr = type.GetCustomAttribute<CmsCollectionAttribute>();
            if (collectionAttr is null) continue;

            var meta = AssemblyLoadFailure.Inspect(type, () => BuildCollection(type, collectionAttr));
            if (!seen.TryAdd(meta.Name, type))
                throw new MetadataException(
                    $"Duplicate collection name '{meta.Name}' (types '{seen[meta.Name].FullName}' and '{type.FullName}').");
            collections.Add(meta);
        }

        ValidateReservedNames(collections);
        ValidateJunctionCollections(collections, typeList);
        ValidateJunctionPrimaryKeys(collections, typeList);
        return ResolveJunctionPayloadFields(collections, typeList);
    }

    /// <summary>
    /// Fail-fast: a field or relation named like a reserved filter token (<c>_and</c>/<c>_or</c>/
    /// <c>_some</c>/<c>_none</c>/<c>_junction</c>, in either spelling) would be unreachable through the
    /// query-string filter grammar — the parser folds it as the token, not as a field name — and would
    /// silently shadow the real query semantics for any predicate mentioning it. Caught at scan time
    /// instead of surfacing as a confusing "unknown field" or mis-parsed query at request time.
    /// </summary>
    private static void ValidateReservedNames(IReadOnlyList<CollectionMetadata> collections)
    {
        foreach (var c in collections)
        {
            var names = c.Fields.Select(f => f.Name).Concat(c.Relations.Select(r => r.Name));
            var clash = names.FirstOrDefault(n => Struo.Application.Query.FilterReservedTokens.All.Contains(n));
            if (clash is not null)
                throw new MetadataException(
                    $"Collection '{c.Name}' declares a field or relation named '{clash}', which is a reserved filter token " +
                    $"({string.Join(", ", Struo.Application.Query.FilterReservedTokens.All)}).");
        }
    }

    /// <summary>
    /// Fail-fast: a M2M junction that is itself a [CmsCollection] must expose both of its foreign keys
    /// as writable [CmsField]s (or as the FK of a many-to-one relation), or items created through the
    /// generic-CRUD API would store empty junction keys. Runs for both <see cref="ScanTypes"/> and the
    /// assembly-based <see cref="Scan"/> (which delegates to it).
    /// </summary>
    private static void ValidateJunctionCollections(IReadOnlyList<CollectionMetadata> collections, IEnumerable<Type> types)
    {
        var byName = collections.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            var owner = byName.GetValueOrDefault(CollectionNameOf(type));
            if (owner is null) continue;
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (NavigateShape.Of(prop) is not { IsJunction: true } nav) continue;
                ValidateOneJunctionCollection(owner, prop, nav, byName);
            }
        }
    }

    // Per-relation body of ValidateJunctionCollections, extracted verbatim (same conditions, order,
    // and exception message) to keep the caller's cognitive complexity in check.
    private static void ValidateOneJunctionCollection(
        CollectionMetadata owner, PropertyInfo prop, NavigateShape nav,
        IReadOnlyDictionary<string, CollectionMetadata> byName)
    {
        var junctionType = nav.JunctionType!;
        var junction = byName.GetValueOrDefault(CollectionNameOf(junctionType));
        if (junction is null) return; // plain junction: nothing to validate

        var writable = junction.Fields.Where(f => !f.IsSystem && !f.ReadOnly).Select(f => f.Name)
            .Concat(junction.Relations.Where(r => r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null).Select(r => r.ForeignKey!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fkA = nav.ParentKey;
        var fkB = nav.TargetKey;
        if (writable.Contains(Camel(fkA)) && writable.Contains(Camel(fkB))) return;

        throw new MetadataException(
            $"Junction collection '{junction.Name}' (used by '{owner.Name}.{Camel(prop.Name)}') must declare its foreign keys " +
            $"'{fkA}' and '{fkB}' as writable [CmsField]s (e.g. Interface = FieldInterface.Uuid); otherwise items created " +
            "through the API store empty keys.");
    }

    /// <summary>
    /// Fail-fast: every M2M junction type — whether it is itself a <c>[CmsCollection]</c> (an exposed
    /// payload junction) or a plain POCO (membership-only) — must declare exactly one
    /// <c>[SugarColumn(IsPrimaryKey = true)]</c> property. <c>ManyToManySync.SyncM2MGenericAsync</c>
    /// resolves the junction's primary key via <c>Columns.Single(c => c.IsPrimarykey)</c> and updates
    /// existing junction rows by that key; a junction with no primary key or a composite one otherwise
    /// throws <see cref="InvalidOperationException"/> at the first M2M write instead of at scan time,
    /// with no startup diagnostic. Runs for both <see cref="ScanTypes"/> and the assembly-based
    /// <see cref="Scan"/> (which delegates to it).
    /// </summary>
    private static void ValidateJunctionPrimaryKeys(IReadOnlyList<CollectionMetadata> collections, IEnumerable<Type> types)
    {
        var byName = collections.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var checkedJunctions = new HashSet<Type>();
        foreach (var type in types)
        {
            var owner = byName.GetValueOrDefault(CollectionNameOf(type));
            if (owner is null) continue;
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (NavigateShape.Of(prop) is not { IsJunction: true } nav) continue;
                ValidateOneJunctionPrimaryKey(owner, prop, nav, checkedJunctions);
            }
        }
    }

    // Per-relation body of ValidateJunctionPrimaryKeys, extracted verbatim (same conditions, order,
    // and exception message) to keep the caller's cognitive complexity in check.
    private static void ValidateOneJunctionPrimaryKey(
        CollectionMetadata owner, PropertyInfo prop, NavigateShape nav, HashSet<Type> checkedJunctions)
    {
        var junctionType = nav.JunctionType!;
        if (!checkedJunctions.Add(junctionType)) return; // already validated via another owner/relation

        var pkCount = junctionType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.GetCustomAttribute<SugarColumn>() is { IsPrimaryKey: true });
        if (pkCount == 1) return;

        throw new MetadataException(
            $"Junction type '{junctionType.Name}' (used by '{owner.Name}.{Camel(prop.Name)}') must declare exactly one " +
            "[SugarColumn(IsPrimaryKey = true)] property; the M2M sync updates junction rows by primary key.");
    }

    /// <summary>
    /// Single source of every M2M relation's junction payload field list (spec U2b §3.1): once all
    /// collections are known, each relation whose junction is itself a [CmsCollection] gets
    /// <see cref="RelationMetadata.JunctionPayloadFields"/> = the junction's non-system, non-readonly fields
    /// minus the two FKs and the SortField property, in field order, hidden ones included.
    /// <c>RelationshipGraph.JunctionPayloadOf</c> consumes this list instead of re-deriving it. Returns new
    /// records (CollectionMetadata/RelationMetadata are immutable) — collections without any junction
    /// collection relation are returned as-is.
    /// </summary>
    private static List<CollectionMetadata> ResolveJunctionPayloadFields(
        IReadOnlyList<CollectionMetadata> collections, IReadOnlyList<Type> types)
    {
        var byName = collections.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var typeByName = types
            .Where(t => t.GetCustomAttribute<CmsCollectionAttribute>() is not null)
            .ToDictionary(CollectionNameOf, StringComparer.OrdinalIgnoreCase);
        return collections
            .Select(c => c.Relations.Any(r => r.JunctionCollection is not null)
                ? c with { Relations = c.Relations.Select(r => WithJunctionPayloadFields(r, typeByName[c.Name], byName)).ToList() }
                : c)
            .ToList();
    }

    private static RelationMetadata WithJunctionPayloadFields(
        RelationMetadata r, Type ownerType, Dictionary<string, CollectionMetadata> byName)
    {
        if (r.JunctionCollection is null || !byName.TryGetValue(r.JunctionCollection, out var junction)) return r;
        var prop = ownerType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .First(p => string.Equals(Camel(p.Name), r.Name, StringComparison.OrdinalIgnoreCase));
        var nav = NavigateShape.Of(prop)!.Value;
        var junctionType = nav.JunctionType!;
        var excluded = new HashSet<string>(StringComparer.Ordinal) { nav.ParentKey, nav.TargetKey };
        if (prop.GetCustomAttribute<CmsRelationAttribute>()?.SortField is { } sortProperty) excluded.Add(sortProperty);
        var props = junctionType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var names = junction.Fields
            .Where(f => !f.IsSystem && !f.ReadOnly)
            .Select(f => (Field: f, Prop: props.FirstOrDefault(p => string.Equals(Camel(p.Name), f.Name, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Prop is not null && !excluded.Contains(x.Prop.Name))
            .Select(x => x.Field.Name)
            .ToList();
        return r with { JunctionPayloadFields = names.Count > 0 ? names : null };
    }

    public static IReadOnlyDictionary<string, EntityDescriptor> ScanDescriptors(IEnumerable<Type> types)
    {
        var map = new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (type.GetCustomAttribute<CmsCollectionAttribute>() is null) continue;

            map[CollectionNameOf(type)] = AssemblyLoadFailure.Inspect(type, () => DescribeEntity(type));
        }
        return map;
    }

    private static EntityDescriptor DescribeEntity(Type type)
    {
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
            var nav = NavigateShape.Of(prop);
            var rel = prop.GetCustomAttribute<CmsRelationAttribute>();
            if (nav is null || rel is null) continue;

            var isCollection = prop.PropertyType.IsGenericType
                && typeof(IEnumerable).IsAssignableFrom(prop.PropertyType);
            if (isCollection) continue; // only many-to-one relations have a local FK

            var fkProp = nav.Value.ForeignKey;
            if (!string.IsNullOrEmpty(fkProp))
                fieldToProp[Camel(fkProp)] = fkProp;
        }

        return new EntityDescriptor(type, fieldToProp, idProperty);
    }

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static readonly Regex ExplicitNamePattern =
        new(@"\A[a-z][A-Za-z0-9]*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Collection name to CLR type for every <c>[CmsCollection]</c> type in <paramref name="types"/>;
    /// the map <see cref="RelationshipGraph"/> is built from.
    /// </summary>
    public static Dictionary<string, Type> CollectionTypeMap(IEnumerable<Type> types) =>
        types
            .Where(t => t.GetCustomAttribute<CmsCollectionAttribute>() is not null)
            .ToDictionary(CollectionNameOf, t => t, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The collection name of a <c>[CmsCollection]</c> type: the attribute's explicit
    /// <c>Name</c> when set (validated), otherwise the camel-cased CLR type name. Every place that maps a
    /// type to its collection goes through here.
    /// </summary>
    public static string CollectionNameOf(Type type)
    {
        var explicitName = type.GetCustomAttribute<CmsCollectionAttribute>()?.Name;
        if (explicitName is null) return Camel(type.Name);
        if (!ExplicitNamePattern.IsMatch(explicitName))
            throw new MetadataException(
                $"Collection type '{type.Name}' declares the invalid name '{explicitName}'; " +
                "a collection name must match ^[a-z][A-Za-z0-9]*$.");
        return explicitName;
    }

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
            // A JSON-column aggregate lives on the parent entity; translating it is not supported.
            if (field.Translatable && FieldInterfaceSets.JsonColumnInterfaces.Contains(field.Interface))
                throw new MetadataException(
                    $"Field '{field.Name}' uses interface '{field.Interface}', which cannot be translatable.");

        var ordered = fields
            .OrderBy(f => f.field.Sort)
            .ThenBy(f => f.order)
            .Select(f => f.field)
            .ToList();

        var name = CollectionNameOf(type);
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
            Creatable = attr.Creatable,
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

        // Without an explicit MaxLength a string field is limited to its column's width, if it has one.
        int? maxLength = attr.MaxLength > 0 ? attr.MaxLength : StringColumnWidth.Of(prop);

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
    private static List<FieldMetadata> BuildRepeaterChildFields(PropertyInfo prop)
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

    private static List<FieldOption> ParseOptions(PropertyInfo prop, CmsOptionsAttribute attr)
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
    /// SqlSugar's <c>Navigate</c> attribute exposes no public properties, so its data is read from
    /// the constructor arguments (<see cref="NavigateShape"/>). Only two constructors are supported;
    /// any other throws <see cref="MetadataException"/>.
    /// </remarks>
    public static IReadOnlyList<RelationMetadata> ScanRelations(Type type)
    {
        var list = new List<RelationMetadata>();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Both [Navigate] and [CmsRelation] required
            var navShape = NavigateShape.Of(prop);
            var rel = prop.GetCustomAttribute<CmsRelationAttribute>();
            if (navShape is null || rel is null) continue;
            var nav = navShape.Value;

            var isCollection = prop.PropertyType.IsGenericType
                && typeof(IEnumerable).IsAssignableFrom(prop.PropertyType);

            Type target;
            RelationKind kind;
            string? fk = null;
            string? junctionCollection = null;

            if (isCollection)
            {
                target = prop.PropertyType.GetGenericArguments()[0];
                kind = nav.IsJunction
                    ? RelationKind.ManyToMany
                    : RelationKind.OneToMany;
                if (kind == RelationKind.OneToMany)
                    // Reverse FK on the child entity (mirrors RelationshipGraph's
                    // ReverseForeignKeyProperty) so the frontend RelatedList knows
                    // which column to filter the child collection by.
                    fk = Camel(nav.ForeignKey);
                else if (nav.JunctionType!.GetCustomAttribute<CmsCollectionAttribute>() is not null)
                    // The M2M junction is itself a [CmsCollection]: its non-FK, non-sort fields are
                    // payload the API reads/writes alongside the relation (see RelationshipGraph).
                    junctionCollection = CollectionNameOf(nav.JunctionType!);
            }
            else
            {
                target = prop.PropertyType.IsGenericType
                    ? prop.PropertyType.GetGenericArguments()[0]
                    : prop.PropertyType;
                // Unwrap Nullable<T>
                target = Nullable.GetUnderlyingType(target) ?? target;
                kind = RelationKind.ManyToOne;
                fk = Camel(nav.ForeignKey);
            }

            list.Add(new RelationMetadata
            {
                Name = Camel(prop.Name),
                Label = prop.Name,
                Kind = kind,
                TargetCollection = CollectionNameOf(target),
                Interface = rel.Interface,
                ForeignKey = fk,
                DisplayTemplate = rel.DisplayTemplate,
                PickerQuery = rel.PickerQuery,
                OnDelete = rel.OnDelete,
                Editable = rel.Editable,
                HelpText = rel.HelpText,
                SelfReferencing = target == type,
                JunctionCollection = junctionCollection,
                SortField = kind == RelationKind.ManyToMany && rel.SortField is not null ? Camel(rel.SortField) : null
            });
        }
        return list;
    }
}

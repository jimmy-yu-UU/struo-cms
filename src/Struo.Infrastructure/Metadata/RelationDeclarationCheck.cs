using System.Reflection;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time checks of what a <c>[CmsRelation]</c> property declares: each key property must exist,
/// spelled exactly, and have a column; a many-to-many names two different key properties; <c>OnDelete</c>
/// belongs to a relation held by a many-to-one key; the junction <c>SortField</c> follows
/// <see cref="SortFieldCheck"/>; and the relation is a public instance property. The relation is named by
/// the class that declares the property.
/// </summary>
internal static class RelationDeclarationCheck
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    // Named by the type that declares the relation: that is the declaration to change.
    public static string RelationName(PropertyInfo relation) => $"{relation.DeclaringType!.Name}.{relation.Name}";

    /// <summary>
    /// The scanned collection class a type is named after, or null when no scanned collection has that
    /// name. The run time resolves a relation's target by collection name, so a plain class named like a
    /// collection stands for the collection's class.
    /// </summary>
    public static Type? CollectionClassOf(Type type, IReadOnlyDictionary<string, Type> collections) =>
        collections.GetValueOrDefault(MetadataScanner.CollectionNameOf(type));

    /// <summary>
    /// How a one-to-many element type led to the collection class <paramref name="resolved"/>, or null when
    /// the class is the element type itself or no scanned collection is named after it.
    /// </summary>
    public static string? ResolutionNote(Type element, Type? resolved)
    {
        if (resolved is null || resolved == element) return null;

        var byName = $"resolves to by name '{MetadataScanner.CollectionNameOf(element)}'";
        return element.Name == resolved.Name
            ? $"the collection class {Qualified(resolved)} that the element type {Qualified(element)} {byName}"
            : $"the collection the element type {Describe(element)} {byName}";
    }

    // A nested type with the class it is declared in, as it is written in the declaration.
    private static string Describe(Type type) =>
        type.DeclaringType is { } outer ? $"{outer.Name}.{type.Name}" : type.Name;

    // The namespace-qualified name: tells apart two classes that share a simple name.
    private static string Qualified(Type type) => type.ToString().Replace('+', '.');

    /// <summary>
    /// The property <paramref name="name"/> of <paramref name="holder"/>, or a <see cref="MetadataException"/>
    /// that names the relation, says what it declared and why the holder has no such property.
    /// </summary>
    public static PropertyInfo RequireProperty(
        PropertyInfo relation, Type holder, string declaration, string name, string kind = "key",
        string? resolution = null) =>
        KeyLookup.Find(holder, name)
        ?? throw new MetadataException(
            $"Relation '{RelationName(relation)}' declares {declaration}, but " +
            KeyLookup.Absence(holder, name, kind, resolution));

    /// <summary>
    /// Every relation of every scanned collection: the keys it names are properties with a column, a
    /// many-to-many names two different keys, <c>OnDelete</c> is declared only on a many-to-one or one-to-one relation,
    /// <c>SortField</c> follows <see cref="SortFieldCheck"/>, and a one-to-many child key is read off the
    /// collection class the element type is named after.
    /// </summary>
    public static void Require(IReadOnlyDictionary<string, Type> collections)
    {
        var relations = collections.Values
            .SelectMany(type => type.GetProperties(PublicInstance))
            .SelectMany(prop => prop.GetCustomAttributes<CmsRelationAttribute>().Take(1)
                .Select(attribute => (Property: prop, Attribute: attribute)));
        foreach (var (prop, attribute) in relations)
            RequireRelation(prop, attribute, collections);
    }

    /// <summary>
    /// A <c>[CmsRelation]</c> property of <paramref name="collection"/> or of a base class is a public
    /// instance property: the scan reads no other.
    /// </summary>
    public static void RequirePublicInstanceRelations(Type collection)
    {
        if (DeclaredProperties.FirstMisplaced(collection, typeof(CmsRelationAttribute)) is not { } misplaced) return;

        throw new MetadataException(
            $"Relation '{RelationName(misplaced)}' has [CmsRelation] but is {DeclaredProperties.WhyNotRead(misplaced)}, " +
            "so the relation is not served. A relation is a public instance property: declare it public and without static.");
    }

    private static void RequireRelation(
        PropertyInfo prop, CmsRelationAttribute attribute, IReadOnlyDictionary<string, Type> collections)
    {
        if (NavigateShape.Of(prop) is not { } nav) return;

        foreach (var (description, holder, key, resolution) in KeyHolders(prop, nav, collections))
        {
            var declaration = $"the {description} '{key}' in [Navigate]";
            RequireColumn(
                prop, holder, declaration, key, "key", "the relation cannot read or store the key", resolution);
        }
        if (nav.IsJunction) RequireDistinctJunctionKeys(prop, nav);
        RequireOnDeleteOnManyToOne(prop, nav, attribute, collections);
        if (attribute.SortField is { } sortField)
            SortFieldCheck.Require(prop, nav, sortField);
    }

    private static void RequireDistinctJunctionKeys(PropertyInfo prop, NavigateShape nav)
    {
        if (!string.Equals(nav.ParentKey, nav.TargetKey, StringComparison.Ordinal)) return;

        throw new MetadataException(
            $"Relation '{RelationName(prop)}' declares the same junction property '{nav.ParentKey}' as parent key " +
            $"and as target key in [Navigate]: a link would store both ids in one column. {nav.JunctionType!.Name} " +
            "needs one key property per side; name a different property for each.");
    }

    private static void RequireOnDeleteOnManyToOne(
        PropertyInfo prop, NavigateShape nav, CmsRelationAttribute attribute,
        IReadOnlyDictionary<string, Type> collections)
    {
        if (attribute.OnDelete == OnDelete.Restrict || !NavigateShape.IsListProperty(prop)) return;

        var kind = nav.IsJunction ? "many-to-many" : "one-to-many";
        var element = prop.PropertyType.GetGenericArguments()[0];
        var remedy = nav.IsJunction
            ? "The links of a deleted item are always removed."
            : $"The many-to-one relation on {(CollectionClassOf(element, collections) ?? element).Name} that " +
              "points at this collection decides what happens to the children when a parent is deleted: " +
              "declare OnDelete there.";
        throw new MetadataException(
            $"Relation '{RelationName(prop)}' declares OnDelete = {attribute.OnDelete} in [CmsRelation], but " +
            "OnDelete decides what happens to the items that point at a deleted target through a many-to-one key, " +
            $"so it has no effect on a {kind} relation. Remove OnDelete from it. {remedy}");
    }

    // The description, the class that holds the property, the property name and how the holder was resolved
    // (null when it was not), for each key a relation declares.
    private static IEnumerable<(string Description, Type Holder, string Key, string? Resolution)> KeyHolders(
        PropertyInfo prop, NavigateShape nav, IReadOnlyDictionary<string, Type> collections)
    {
        if (!NavigateShape.IsListProperty(prop))
        {
            yield return ("foreign key", prop.DeclaringType!, nav.ForeignKey, null);
        }
        else if (nav.IsJunction)
        {
            yield return ("junction parent key", nav.JunctionType!, nav.ParentKey, null);
            yield return ("junction target key", nav.JunctionType!, nav.TargetKey, null);
        }
        else
        {
            var element = prop.PropertyType.GetGenericArguments()[0];
            var resolved = CollectionClassOf(element, collections);
            yield return ("foreign key", resolved ?? element, nav.ForeignKey, ResolutionNote(element, resolved));
        }
    }

    /// <summary>
    /// The property <paramref name="name"/> of <paramref name="holder"/> when it exists and has a column;
    /// otherwise a <see cref="MetadataException"/> that names the relation and what to declare.
    /// </summary>
    public static PropertyInfo RequireColumn(
        PropertyInfo relation, Type holder, string declaration, string name, string kind, string consequence,
        string? resolution = null)
    {
        var property = RequireProperty(relation, holder, declaration, name, kind, resolution);
        if (!KeyLookup.HasNoColumn(property)) return property;

        throw new MetadataException(
            $"Relation '{RelationName(relation)}' declares {declaration}, but "
            + $"{property.DeclaringType!.Name}.{property.Name} " +
            $"is [SugarColumn(IsIgnore = true)]: it has no column, so {consequence}. " +
            "Name a property that is stored in a column, or remove IsIgnore from it." +
            (resolution is null ? "" : $" The key is read off {KeyLookup.Label(holder, resolution)}."));
    }
}

using System.Reflection;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time checks of what a <c>[CmsRelation]</c> property names: each key property and the
/// junction <c>SortField</c> must exist, spelled exactly, and have a column, and a <c>SortField</c> belongs
/// to a many-to-many relation. The relation is named by the class that declares the property.
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
    public static string? ResolutionNote(Type element, Type? resolved) =>
        resolved is null || resolved == element
            ? null
            : "the collection the element type " + Describe(element) +
              $" resolves to by name '{MetadataScanner.CollectionNameOf(element)}'";

    // A nested type with the class it is declared in, as it is written in the declaration.
    private static string Describe(Type type) =>
        type.DeclaringType is { } outer ? $"{outer.Name}.{type.Name}" : type.Name;

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
    /// Every relation of every scanned collection: the keys it names and its <c>SortField</c> are properties
    /// with a column, and a one-to-many child key is read off the collection class the element type is
    /// named after.
    /// </summary>
    public static void Require(IReadOnlyDictionary<string, Type> collections)
    {
        var relations = collections.Values
            .SelectMany(type => type.GetProperties(PublicInstance))
            .Where(prop => prop.IsDefined(typeof(CmsRelationAttribute), inherit: true));
        foreach (var prop in relations)
            RequireRelation(prop, collections);
    }

    private static void RequireRelation(PropertyInfo prop, IReadOnlyDictionary<string, Type> collections)
    {
        if (NavigateShape.Of(prop) is not { } nav) return;

        foreach (var (description, holder, key, resolution) in KeyHolders(prop, nav, collections))
        {
            var declaration = $"the {description} '{key}' in [Navigate]";
            RequireColumn(
                prop, holder, declaration, key, "key", "the relation cannot read or store the key", resolution);
        }
        if (prop.GetCustomAttribute<CmsRelationAttribute>()?.SortField is { } sortField)
            RequireSortField(prop, nav, sortField);
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

    private static void RequireSortField(PropertyInfo prop, NavigateShape nav, string sortField)
    {
        if (!nav.IsJunction)
            throw new MetadataException(
                $"Relation '{RelationName(prop)}' declares SortField '{sortField}', but SortField orders the links of "
                + "a many-to-many relation and this relation is not one. Remove SortField from it.");

        RequireColumn(
            prop, nav.JunctionType!, $"SortField '{sortField}' in [CmsRelation]", sortField, "sort field",
            "the links cannot be ordered by it");
    }

    private static void RequireColumn(
        PropertyInfo relation, Type holder, string declaration, string name, string kind, string consequence,
        string? resolution = null)
    {
        var property = RequireProperty(relation, holder, declaration, name, kind, resolution);
        if (!KeyLookup.HasNoColumn(property)) return;

        throw new MetadataException(
            $"Relation '{RelationName(relation)}' declares {declaration}, but "
            + $"{property.DeclaringType!.Name}.{property.Name} " +
            $"is [SugarColumn(IsIgnore = true)]: it has no column, so {consequence}. " +
            "Name a property that is stored in a column, or remove IsIgnore from it.");
    }
}

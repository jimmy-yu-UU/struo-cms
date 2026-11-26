using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a property overriding a base property which carries <c>[SugarColumn]</c>
/// repeats the attribute. SqlSugar reads only the override's own declaration, so an override without one
/// has a column the base declaration does not describe. It looks at the public instance properties the
/// scanned type exposes, the ones the scan and the ORM read.
/// </summary>
internal static class ColumnOverrideCheck
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// Refuses a public instance property of <paramref name="type"/> that overrides a base property and
    /// leaves out the <c>[SugarColumn]</c> the base declaration carries. When
    /// <paramref name="primaryKeyRuleReports"/> is set, an override of a marked primary key on a type with
    /// no primary key is left to the primary key rule, which names the property to mark.
    /// </summary>
    public static void Require(Type type, bool primaryKeyRuleReports)
    {
        var noPrimaryKey = primaryKeyRuleReports && KeyLookup.PrimaryKeyProperties(type).Count == 0;
        foreach (var prop in type.GetProperties(PublicInstance))
        {
            if (HasOwnColumn(prop) || CarrierOf(prop) is not { } carrier) continue;
            if (noPrimaryKey && IsPrimaryKey(carrier)) continue;
            throw Refusal(type, prop, carrier);
        }
    }

    private static MetadataException Refusal(Type type, PropertyInfo prop, PropertyInfo carrier)
    {
        var name = $"{prop.DeclaringType!.Name}.{prop.Name}";
        var inherited = prop.DeclaringType == type ? "" : $", which '{type.Name}' inherits";
        return new MetadataException(
            $"Property '{name}' overrides '{carrier.DeclaringType!.Name}.{carrier.Name}', which carries " +
            "[SugarColumn], without repeating it. SqlSugar reads only the override's own [SugarColumn], " +
            $"so the column would not follow the base declaration. Repeat [SugarColumn(...)] on '{name}'{inherited}.");
    }

    private static bool HasOwnColumn(PropertyInfo prop) => prop.IsDefined(typeof(SugarColumn), inherit: false);

    private static bool IsPrimaryKey(PropertyInfo prop) =>
        prop.GetCustomAttributes(typeof(SugarColumn), inherit: false).OfType<SugarColumn>().Any(a => a.IsPrimaryKey);

    // The nearest base declaration of the same slot that carries [SugarColumn] on its own declaration;
    // null for a property that overrides nothing, or whose bases carry none.
    private static PropertyInfo? CarrierOf(PropertyInfo prop) =>
        DeclaredProperties.Hierarchy(prop.DeclaringType!).Skip(1)
            .SelectMany(DeclaredProperties.DeclaredOn)
            .FirstOrDefault(p => HasOwnColumn(p) && DeclaredProperties.SameSlot(prop, p));
}

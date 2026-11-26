using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a property overriding a base property which carries <c>[SugarColumn]</c>
/// repeats the attribute. SqlSugar reads only the override's own declaration, so an override without one
/// has a column the base declaration does not describe.
/// </summary>
internal static class ColumnOverrideCheck
{
    /// <summary>
    /// Refuses an override of <paramref name="type"/> or of a base class that leaves out the
    /// <c>[SugarColumn]</c> its base declaration carries. When <paramref name="primaryKeyRuleReports"/> is
    /// set, an override of a marked primary key on a type with no primary key is left to the primary key
    /// rule, which names the property to mark.
    /// </summary>
    public static void Require(Type type, bool primaryKeyRuleReports)
    {
        var noPrimaryKey = primaryKeyRuleReports && KeyLookup.PrimaryKeyProperties(type).Count == 0;
        foreach (var prop in DeclaredProperties.Hierarchy(type).SelectMany(DeclaredProperties.DeclaredOn))
        {
            if (HasOwnColumn(prop) || CarrierOf(prop) is not { } carrier) continue;
            if (noPrimaryKey && IsPrimaryKey(carrier)) continue;
            throw Refusal(prop, carrier);
        }
    }

    private static MetadataException Refusal(PropertyInfo prop, PropertyInfo carrier)
    {
        var name = $"{prop.DeclaringType!.Name}.{prop.Name}";
        return new MetadataException(
            $"Property '{name}' overrides '{carrier.DeclaringType!.Name}.{carrier.Name}', which carries " +
            "[SugarColumn], without repeating it. SqlSugar reads only the override's own [SugarColumn], " +
            $"so the column would not follow the base declaration. Repeat [SugarColumn(...)] on '{name}'.");
    }

    private static bool HasOwnColumn(PropertyInfo prop) => prop.IsDefined(typeof(SugarColumn), inherit: false);

    private static bool IsPrimaryKey(PropertyInfo prop) =>
        prop.GetCustomAttributes(typeof(SugarColumn), inherit: false).OfType<SugarColumn>().Any(a => a.IsPrimaryKey);

    // The nearest base declaration of the overridden property that carries [SugarColumn] on its own
    // declaration; null for a property that overrides nothing, or whose bases carry none.
    private static PropertyInfo? CarrierOf(PropertyInfo prop)
    {
        var roots = DeclaredProperties.AccessorsOf(prop).Select(a => a.GetBaseDefinition()).ToList();
        if (!roots.Exists(root => root.DeclaringType != prop.DeclaringType)) return null;

        return DeclaredProperties.Hierarchy(prop.DeclaringType!).Skip(1)
            .SelectMany(DeclaredProperties.DeclaredOn)
            .FirstOrDefault(p => HasOwnColumn(p) && Declares(p, roots));
    }

    private static bool Declares(PropertyInfo prop, List<MethodInfo> roots) =>
        DeclaredProperties.AccessorsOf(prop).Any(a => roots.Contains(a.GetBaseDefinition()));
}

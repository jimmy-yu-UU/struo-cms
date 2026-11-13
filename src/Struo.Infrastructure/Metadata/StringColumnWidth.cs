using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The declared width of a string property's column. <c>MetadataScanner</c> uses it as the default
/// <c>MaxLength</c> of a field that sets none, and <c>SqlSugarClientFactory</c> sets the column's length
/// from it, so the write-side limit and the column cannot disagree.
/// </summary>
internal static class StringColumnWidth
{
    /// <summary>SqlSugar's width for a string column that declares none (<c>varchar(255)</c>).</summary>
    internal const int Default = 255;

    /// <summary>
    /// The width of the column <paramref name="property"/> maps to, or null when the property is not a
    /// string or its column has no length to state: a <c>[ColumnShape]</c>, a JSON or content-bearing
    /// <c>[CmsField]</c> interface, <c>IsJson</c>, or an explicit <c>ColumnDataType</c> without a
    /// <c>Length</c>. An explicit <c>ColumnDataType</c> with a <c>Length</c> is that <c>Length</c> wide.
    /// </summary>
    internal static int? Of(PropertyInfo property)
    {
        if (property.PropertyType != typeof(string)) return null;
        if (property.GetCustomAttribute<ColumnShapeAttribute>() is not null) return null;

        var column = property.GetCustomAttribute<SugarColumn>();
        if (column is { IsJson: true }) return null;

        var field = property.GetCustomAttribute<CmsFieldAttribute>();
        if (field is not null
            && (FieldInterfaceSets.JsonColumnInterfaces.Contains(field.Interface)
                || FieldInterfaceSets.ContentBearingInterfaces.Contains(field.Interface)))
            return null;

        var length = column is { Length: > 0 } ? column.Length : (int?)null;
        return string.IsNullOrEmpty(column?.ColumnDataType) ? length ?? Default : length;
    }
}

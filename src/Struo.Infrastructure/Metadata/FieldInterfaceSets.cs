using System.Collections.Frozen;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The <see cref="FieldInterface"/> groupings that more than one place in Infrastructure reads.
/// <c>FieldInterfaceSetsTests</c> pins every enum member's membership, so a new member has to be
/// placed in a group (or in none) explicitly.
/// </summary>
internal static class FieldInterfaceSets
{
    /// <summary>
    /// Structured aggregates stored in a JSON column on the parent entity (a <c>List&lt;&gt;</c> or
    /// <c>Dictionary&lt;&gt;</c> that SqlSugar (de)serializes). The scanner refuses to make them
    /// translatable; <c>SqlSugarClientFactory</c> maps them to a JSON LongText column. <c>Json</c> is
    /// not here: it is a string holding raw JSON text and counts as content-bearing.
    /// </summary>
    internal static readonly FrozenSet<FieldInterface> JsonColumnInterfaces = new[]
    {
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags,
        FieldInterface.KeyValue, FieldInterface.Files, FieldInterface.Repeater
    }.ToFrozenSet();

    /// <summary>String interfaces whose content is unbounded by nature; their column is LongText.</summary>
    internal static readonly FrozenSet<FieldInterface> ContentBearingInterfaces = new[]
    {
        FieldInterface.RichText, FieldInterface.Textarea, FieldInterface.Markdown,
        FieldInterface.Code, FieldInterface.Json
    }.ToFrozenSet();
}

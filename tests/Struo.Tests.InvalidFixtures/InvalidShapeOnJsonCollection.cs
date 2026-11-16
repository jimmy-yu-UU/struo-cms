using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Persistence;

namespace Struo.Tests.InvalidFixtures;

/// <summary>
/// A collection whose JSON-interface property also carries <c>[ColumnShape]</c>. No host registers this
/// assembly; a test names it as a content assembly to see the startup failure.
/// </summary>
[SugarTable("ifx_shape_on_json")]
[CmsCollection("Shape on JSON", Name = "ifxShapeOnJson")]
public sealed class InvalidShapeOnJsonCollection
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [ColumnShape(ColumnShape.LongText)]
    [CmsField(Label = "Tags", Interface = FieldInterface.Tags)]
    public List<string> Tags { get; set; } = [];
}

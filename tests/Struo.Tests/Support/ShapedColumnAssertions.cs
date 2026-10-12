using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;

namespace Struo.Tests.Support;

/// <summary>
/// Asserts that a column read back from the catalog has the type <see cref="ColumnTypeMap"/> declares for a
/// shape. The catalog reports a parenthesised literal as its type name plus a length (<c>nvarchar(max)</c>
/// is <c>nvarchar</c> with length -1, <c>datetime(6)</c> is <c>datetime</c> with length 6), so both are compared.
/// </summary>
internal static class ShapedColumnAssertions
{
    public static void ShouldHaveShape(this DbColumnInfo column, ColumnShape shape, StruoDbType dbType)
    {
        var (type, length) = Reported(ColumnTypeMap.For(shape, DbTypeMapper.Map(dbType)));
        column.DataType.Should().BeEquivalentTo(type, $"column {column.DbColumnName} is declared {shape}");
        if (length is { } expected)
            column.Length.Should().Be(expected, $"column {column.DbColumnName} is declared {shape}");
    }

    internal static (string Type, int? Length) Reported(string literal)
    {
        var open = literal.IndexOf('(');
        if (open < 0) return (literal, null);
        var argument = literal[(open + 1)..^1];
        var length = argument.Equals("max", StringComparison.OrdinalIgnoreCase) ? -1 : int.Parse(argument);
        return (literal[..open], length);
    }
}

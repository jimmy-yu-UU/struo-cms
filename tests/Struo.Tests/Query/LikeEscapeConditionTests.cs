// tests/Struo.Tests/Query/LikeEscapeConditionTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// The condition a string operator builds for each backend type, with no database: its condition type and
/// the exact value bound to it. The backslash form is used by PostgreSQL and MySQL (MariaDB reaches it
/// as MySQL), the bracket form by SQL Server, and any other type keeps the value as written.
/// </summary>
public sealed class LikeEscapeConditionTests
{
    private static readonly string Bs = new((char)92, 1);
    private static readonly string Bs2 = new((char)92, 2);

    private static readonly (QueryOperator Op, ConditionalType Type)[] Operators =
    [
        (QueryOperator.Contains, ConditionalType.Like),
        (QueryOperator.StartsWith, ConditionalType.LikeLeft),
        (QueryOperator.EndsWith, ConditionalType.LikeRight),
    ];

    // value, backslash form, bracket form
    public static TheoryData<string, string, string> Values() => new()
    {
        { "100%", "100" + Bs + "%", "100[%]" },
        { "a_c", "a" + Bs + "_c", "a[_]c" },
        { "[ab]", "[ab]", "[[]ab]" },
        { Bs, Bs2, Bs },
        { "d" + Bs, "d" + Bs2, "d" + Bs },
        { "a%_[" + Bs, "a" + Bs + "%" + Bs + "_[" + Bs2, "a[%][_][[]" + Bs },
        { "abc", "abc", "abc" },
        { "", "", "" },
    };

    private static ValueTarget Target(SqlSugar.DbType db) =>
        new("Name", "name", typeof(string), db);

    private static ConditionalModel Build(SqlSugar.DbType db, QueryOperator op, string value) =>
        (ConditionalModel)ValueConditions.Build(Target(db), op, value)!;

    [Theory]
    [MemberData(nameof(Values))]
    public void PostgreSQL_and_MySQL_bind_the_value_with_backslashes(string value, string backslashForm, string bracketForm)
    {
        _ = bracketForm;
        foreach (var db in new[] { SqlSugar.DbType.PostgreSQL, SqlSugar.DbType.MySql })
            foreach (var (op, type) in Operators)
            {
                var model = Build(db, op, value);
                model.ConditionalType.Should().Be(type);
                model.FieldName.Should().Be("Name");
                model.FieldValue.Should().Be(backslashForm, $"{db} {op}");
                model.CustomConditionalFunc.Should().BeNull();
            }
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void SQL_Server_binds_the_value_with_brackets(string value, string backslashForm, string bracketForm)
    {
        _ = backslashForm;
        foreach (var (op, type) in Operators)
        {
            var model = Build(SqlSugar.DbType.SqlServer, op, value);
            model.ConditionalType.Should().Be(type);
            model.FieldValue.Should().Be(bracketForm, op.ToString());
            model.CustomConditionalFunc.Should().BeNull();
        }
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void An_unlisted_backend_type_keeps_the_value_as_written(string value, string backslashForm, string bracketForm)
    {
        _ = backslashForm;
        _ = bracketForm;
        foreach (var (op, type) in Operators)
        {
            var model = Build(SqlSugar.DbType.Oracle, op, value);
            model.ConditionalType.Should().Be(type);
            model.FieldValue.Should().Be(value);
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("[ab]")]
    [InlineData("a[b")]
    public void SQLite_keeps_a_value_without_a_percent_sign_or_underscore_as_written(string value)
    {
        foreach (var (op, type) in Operators)
        {
            var model = Build(SqlSugar.DbType.Sqlite, op, value);
            model.ConditionalType.Should().Be(type);
            model.FieldValue.Should().Be(value);
            model.CustomConditionalFunc.Should().BeNull();
        }
        Build(SqlSugar.DbType.Sqlite, QueryOperator.EndsWith, Bs).FieldValue.Should().Be(Bs);
    }

    [Theory]
    [InlineData("100%")]
    [InlineData("a_c")]
    public void SQLite_with_a_percent_sign_or_underscore_and_no_key_property_is_an_error_and_never_a_wildcard(string value)
    {
        var act = () => ValueConditions.Build(Target(SqlSugar.DbType.Sqlite), QueryOperator.Contains, value);
        act.Should().Throw<InvalidOperationException>().WithMessage("*no key property*");
    }

    [Fact]
    public void The_escapes_apply_to_a_JSON_column_the_same_way()
    {
        var json = new ValueTarget("Data", "data", typeof(List<string>), SqlSugar.DbType.PostgreSQL, StoredAsJson: true);
        ((ConditionalModel)ValueConditions.Build(json, QueryOperator.Contains, "a_c")!).FieldValue.Should().Be("a" + Bs + "_c");
    }
}

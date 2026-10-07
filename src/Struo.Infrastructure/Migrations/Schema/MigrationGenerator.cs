using System.Globalization;
using System.Text;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>What to generate: a class, its version, an optional namespace, and the table to create (null for an empty skeleton).</summary>
public sealed record MigrationSpec(string ClassName, long Version, string? Namespace, ExpectedTable? Table);

/// <summary>Writes the C# source of a FluentMigrator migration that creates one entity's table.</summary>
public static class MigrationGenerator
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };

    public static string FileName(MigrationSpec spec) =>
        string.Create(CultureInfo.InvariantCulture, $"{spec.Version}_{spec.ClassName}.cs");

    /// <summary>True for a C# identifier that is not a reserved keyword.</summary>
    public static bool IsValidClassName(string name) =>
        !string.IsNullOrEmpty(name)
        && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
        && !Keywords.Contains(name);

    /// <exception cref="NotSupportedException">A column has no portable FluentMigrator mapping.</exception>
    public static string Generate(MigrationSpec spec)
    {
        var up = spec.Table is null ? [] : UpLines(spec.Table);
        var down = spec.Table is null ? [] : new[] { $"Delete.Table({TableExpression(spec.Table)});" };

        var sb = new StringBuilder();
        sb.Append("using FluentMigrator;\nusing Struo.Infrastructure.Migrations;\n");
        if (up.Any(l => l.Contains("ColumnShape.", StringComparison.Ordinal)))
            sb.Append("using Struo.Infrastructure.Persistence;\n");
        sb.Append('\n');
        if (spec.Namespace is not null)
            sb.Append("namespace ").Append(spec.Namespace).Append(";\n\n");
        sb.Append(string.Create(CultureInfo.InvariantCulture,
            $"[Migration({spec.Version}, {Lit(spec.ClassName)})]\n"));
        sb.Append("public sealed class ").Append(spec.ClassName).Append(" : StruoMigration\n{\n");
        AppendMethod(sb, "Up", up);
        sb.Append('\n');
        AppendMethod(sb, "Down", down);
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void AppendMethod(StringBuilder sb, string name, IReadOnlyList<string> lines)
    {
        sb.Append("    public override void ").Append(name).Append("()\n    {\n");
        foreach (var line in lines)
            sb.Append(line.Length == 0 ? "" : "        ").Append(line).Append('\n');
        sb.Append("    }\n");
    }

    private static List<string> UpLines(ExpectedTable table)
    {
        var tableExpr = TableExpression(table);
        var lines = new List<string> { $"Create.Table({tableExpr})" };
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var end = i == table.Columns.Count - 1 ? ";" : "";
            lines.Add($"    {ColumnChain(table.Columns[i])}{end}");
        }
        if (table.Columns.Count == 0)
            lines[0] += ";";

        foreach (var index in table.Indexes)
        {
            lines.Add("");
            lines.AddRange(IndexLines(table, tableExpr, index));
        }
        return lines;
    }

    private static string TableExpression(ExpectedTable table) =>
        table.IsFrameworkTable ? $"FrameworkTable({Lit(table.LogicalName)})" : Lit(table.PhysicalName);

    private static string ColumnChain(ExpectedColumn c)
    {
        var sb = new StringBuilder($".WithColumn({Lit(c.Name)}){TypeCall(c)}");
        if (c.IsPrimaryKey) sb.Append(".PrimaryKey()");
        if (c.IsIdentity) sb.Append(".Identity()");
        if (!c.IsPrimaryKey) sb.Append(c.IsNullable ? ".Nullable()" : ".NotNullable()");
        return sb.ToString();
    }

    private static string TypeCall(ExpectedColumn c) => c.Category switch
    {
        ColumnCategory.String => string.Create(CultureInfo.InvariantCulture, $".AsString({c.Length ?? 255})"),
        ColumnCategory.LongText => c.IsJson ? ".AsJson(Db)" : ".AsLongText(Db)",
        ColumnCategory.DateTimeWithTimeZone => ".AsShape(ColumnShape.TimestampWithTimeZone, Db)",
        ColumnCategory.Integer => ".AsInt32()",
        ColumnCategory.BigInteger => ".AsInt64()",
        ColumnCategory.Decimal => ".AsDecimal()",
        ColumnCategory.Boolean => ".AsBoolean()",
        ColumnCategory.Guid => ".AsGuid()",
        ColumnCategory.DateTime => ".AsDateTime()",
        ColumnCategory.Binary => ".AsBinary(int.MaxValue)",
        _ => throw new NotSupportedException($"Column '{c.Name}' has no portable mapping")
    };

    private static IEnumerable<string> IndexLines(ExpectedTable table, string tableExpr, ExpectedIndex index)
    {
        if (table.TranslationLogicalTable is not null && index.Columns.Count == 2
            && index.Name == TranslationSidecarIndexPolicy.IndexNameFor(table.TranslationLogicalTable))
        {
            yield return $"CreateTranslationUniqueIndex({Lit(table.TranslationLogicalTable)}, {tableExpr}, " +
                         $"{Lit(index.Columns[0])}, {Lit(index.Columns[1])});";
            yield break;
        }

        yield return $"Create.Index({IndexNameExpression(table, index.Name)}).OnTable({tableExpr})";
        for (var i = 0; i < index.Columns.Count; i++)
        {
            var last = i == index.Columns.Count - 1 && !index.IsUnique;
            yield return $"    .OnColumn({Lit(index.Columns[i])}).Ascending(){(last ? ";" : "")}";
        }
        if (index.IsUnique)
            yield return "    .WithOptions().Unique();";
    }

    private static string IndexNameExpression(ExpectedTable table, string name)
    {
        var at = table.IsFrameworkTable ? name.IndexOf(table.PhysicalName, StringComparison.Ordinal) : -1;
        if (at < 0)
            return Lit(name);
        var head = name[..at];
        var tail = name[(at + table.PhysicalName.Length)..];
        return $"$\"{Escape(head)}{{FrameworkTable({Lit(table.LogicalName)})}}{Escape(tail)}\"";
    }

    private static string Lit(string s) => $"\"{Escape(s)}\"";

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

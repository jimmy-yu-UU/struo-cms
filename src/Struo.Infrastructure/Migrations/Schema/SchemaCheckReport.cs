using System.Text;

namespace Struo.Infrastructure.Migrations.Schema;

public enum FindingSeverity { Error, Warning }

public enum FindingKind
{
    MissingTable,
    MissingColumn,
    NullabilityMismatch,
    TypeMismatch,
    LengthMismatch,
    MissingUniqueIndex,
    UndeclaredColumn,
    IndexCheckUnsupported,
    TypeCheckUnsupported
}

/// <summary>One difference between the live database and the entity metadata. <c>Column</c> is null for table-level findings.</summary>
public sealed record SchemaFinding(FindingSeverity Severity, FindingKind Kind, string Table, string? Column, string Message);

/// <summary>The outcome of a schema check: every finding, with errors and warnings separated.</summary>
public sealed record SchemaCheckReport(IReadOnlyList<SchemaFinding> Findings)
{
    public IReadOnlyList<SchemaFinding> Errors => Findings.Where(f => f.Severity == FindingSeverity.Error).ToList();

    public IReadOnlyList<SchemaFinding> Warnings => Findings.Where(f => f.Severity == FindingSeverity.Warning).ToList();

    public bool HasErrors => Findings.Any(f => f.Severity == FindingSeverity.Error);

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var f in Findings)
        {
            var label = f.Severity == FindingSeverity.Error ? "ERROR" : "WARNING";
            var target = f.Column is null ? f.Table : $"{f.Table}.{f.Column}";
            text.AppendLine($"{label} {target}: {f.Message}");
        }
        text.Append($"{Errors.Count} error(s), {Warnings.Count} warning(s)");
        return text.ToString();
    }
}

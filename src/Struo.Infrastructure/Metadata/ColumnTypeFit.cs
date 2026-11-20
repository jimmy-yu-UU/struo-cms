using System.Text;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The closed table of explicit <c>ColumnDataType</c> names that contradict the property's CLR type and so
/// misbehave at run time. A name that is not in the table is the fork's own and passes.
/// </summary>
internal static class ColumnTypeFit
{
    internal readonly record struct Misfit(string Fix, string Why);

    private const string ZoneFix = "Declare [ColumnShape(ColumnShape.TimestampWithTimeZone)] and remove ColumnDataType";
    private const string ZoneWhy =
        "only the shape makes the framework read and write the column as a UTC instant on PostgreSQL and SQL Server, " +
        "and without it a facet of the column and a facet of a zone-less DateTime column fail each other on SQL Server";
    private const string WidthWhy =
        "on SQL Server a facet of this column fails when a facet of a column of the property's own width ran first, " +
        "and the check is the same on every backend";
    private const string BooleanFix = "Write tinyint without its length, or remove ColumnDataType";
    private const string BooleanWhy =
        "MySQL and MariaDB read a tinyint(1) column as a boolean, so a stored 7 comes back as 1, " +
        "and the check is the same on every backend";

    // One type name as written in a column type: the words it is made of, and for a one-word name the length
    // or precision that must follow it (null when any, or none, matches).
    private sealed record Entry(string[] Words, string? FirstArgument = null);

    private sealed record Rule(Type Clr, Entry[] Entries, Misfit Misfit);

    private static Entry Name(string words, string? firstArgument = null) => new(words.Split(' '), firstArgument);

    private static readonly Rule[] Rules =
    [
        new(typeof(DateTime), [Name("timestamptz"), Name("timestamp with time zone"), Name("datetimeoffset")], new(ZoneFix, ZoneWhy)),
        new(typeof(int), [Name("bigint")], new("Declare the property as long{?}, or remove ColumnDataType", WidthWhy)),
        new(typeof(int), [Name("smallint")], new("Remove ColumnDataType, or declare the column as int", WidthWhy)),
        new(typeof(long), [Name("int"), Name("integer")], new("Declare the property as int{?}, or remove ColumnDataType", WidthWhy)),
        new(typeof(long), [Name("smallint")], new("Remove ColumnDataType, or declare the column as bigint", WidthWhy)),
        new(typeof(int), [Name("tinyint", "1")], new(BooleanFix, BooleanWhy)),
        new(typeof(long), [Name("tinyint", "1")], new(BooleanFix, BooleanWhy)),
    ];

    /// <summary>
    /// The contradiction between <paramref name="propertyType"/> (<c>Nullable&lt;T&gt;</c> counts as
    /// <c>T</c>) and the column type, or null when the pair is not in the table. The column type is judged
    /// by its leading words, so <c>bigint not null</c> and <c>int identity(1,1)</c> count as written.
    /// </summary>
    internal static Misfit? Find(Type propertyType, string? columnDataType)
    {
        if (string.IsNullOrWhiteSpace(columnDataType)) return null;

        var underlying = Nullable.GetUnderlyingType(propertyType);
        var single = underlying ?? propertyType;
        var tokens = Tokenize(columnDataType);

        foreach (var rule in Rules)
        {
            if (rule.Clr != single || !rule.Entries.Any(entry => Matches(entry, tokens))) continue;
            return rule.Misfit with { Fix = rule.Misfit.Fix.Replace("{?}", underlying is null ? "" : "?", StringComparison.Ordinal) };
        }

        return null;
    }

    private readonly record struct Token(string Word, string? Argument);

    private static bool Matches(Entry entry, List<Token> tokens)
    {
        var count = entry.Words.Length;
        if (tokens.Count < count) return false;
        for (var i = 0; i < count; i++)
            if (tokens[i].Word != entry.Words[i]) return false;
        if (entry.FirstArgument is not null && tokens[0].Argument != entry.FirstArgument) return false;

        // "unsigned" and "zerofill" turn the name into another type; any other trailing word is a column modifier.
        return tokens.Count == count || tokens[count].Word is not ("unsigned" or "zerofill");
    }

    // Lower-case words split at whitespace and at a parenthesised group; the group's text, without spaces,
    // is the argument of the word before it. Brackets and quotes around a name are dropped:
    // "[datetimeoffset](7)" is the word "datetimeoffset" with the argument "7".
    private static List<Token> Tokenize(string columnType)
    {
        var tokens = new List<Token>();
        var word = new StringBuilder();
        for (var i = 0; i < columnType.Length; i++)
        {
            var c = columnType[i];
            if (c == '(')
            {
                var end = ClosingParenthesis(columnType, i);
                Flush(tokens, word, Compact(columnType[(i + 1)..end]));
                i = end;
            }
            else if (char.IsWhiteSpace(c)) Flush(tokens, word, null);
            else if (c is not ('[' or ']' or '"' or '`')) word.Append(char.ToLowerInvariant(c));
        }

        Flush(tokens, word, null);
        return tokens;
    }

    private static int ClosingParenthesis(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }

        return text.Length;
    }

    private static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private static void Flush(List<Token> tokens, StringBuilder word, string? argument)
    {
        if (word.Length > 0)
        {
            tokens.Add(new Token(word.ToString(), argument));
            word.Clear();
        }
        else if (argument is not null && tokens.Count > 0 && tokens[^1].Argument is null)
        {
            tokens[^1] = tokens[^1] with { Argument = argument };
        }
    }
}

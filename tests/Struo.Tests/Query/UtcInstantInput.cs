namespace Struo.Tests.Query;

/// <summary>
/// The filter value forms that name 2026-03-04T05:06:07Z: REST strings (Z, offset, zone-less) and the
/// <see cref="DateTimeOffset"/>/<see cref="DateTime"/> values GraphQL and callers hand over. Theories take
/// the form name so their data stays serializable.
/// </summary>
internal static class UtcInstantInput
{
    public static readonly DateTime Instant = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    public static readonly string[] Forms = ["z", "offset", "zoneless", "dateTimeOffset", "local", "utc"];

    public static object Of(string form) => form switch
    {
        "z" => "2026-03-04T05:06:07Z",
        "offset" => "2026-03-04T13:06:07+08:00",
        "zoneless" => "2026-03-04T05:06:07",
        "dateTimeOffset" => new DateTimeOffset(2026, 3, 4, 13, 6, 7, TimeSpan.FromHours(8)),
        "local" => Instant.ToLocalTime(),
        "utc" => Instant,
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Unknown form."),
    };
}

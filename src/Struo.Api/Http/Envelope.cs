using System.Text.Json.Serialization;

namespace Struo.Api.Http;

public sealed record ValidationDetail(string Field, string Message);

public sealed record ErrorBody(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ValidationDetail>? Details = null);

public sealed record MetaInfo(long Total, int Limit, int Offset);

/// <summary>Success wire envelope; <c>Meta</c> is omitted from JSON when null.</summary>
public sealed record SuccessEnvelope(
    bool Success,
    object? Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    MetaInfo? Meta = null);

public sealed record ErrorEnvelope(bool Success, ErrorBody Error);

/// <summary>Marker a controller returns for a paginated list; the result filter unwraps it to data + meta.</summary>
public sealed record PagedResult(object Data, long Total, int Limit, int Offset);

public static class Envelope
{
    public static SuccessEnvelope Success(object? data, MetaInfo? meta = null) => new(true, data, meta);

    public static ErrorEnvelope Error(string code, string message, IReadOnlyList<ValidationDetail>? details = null) =>
        new(false, new ErrorBody(code, message, details));
}

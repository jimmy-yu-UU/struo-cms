using System.Text.Json.Serialization;
using Struo.Domain.Query;

namespace Struo.Api.Http;

public sealed record ValidationDetail(
    string Field,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Locale = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? MaxBytes = null);

public sealed record ErrorBody(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ValidationDetail>? Details = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TraceId = null);

public sealed record MetaInfo(
    long Total, int Limit, int Offset,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, IReadOnlyList<FacetBucket>>? Facets = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? Aggregate = null);

/// <summary>Success wire envelope; <c>Meta</c> is omitted from JSON when null.</summary>
public sealed record SuccessEnvelope(
    bool Success,
    object? Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    MetaInfo? Meta = null);

/// <summary>Meta of a save that reports the new optimistic-concurrency version of the record it guards.</summary>
public sealed record VersionMeta(long Version);

/// <summary>Success wire envelope whose <c>meta</c> carries only the new <c>version</c>.</summary>
public sealed record VersionedSuccessEnvelope(bool Success, object? Data, VersionMeta Meta);

public sealed record ErrorEnvelope(bool Success, ErrorBody Error);

/// <summary>Marker a controller returns for a paginated list; the result filter unwraps it to data + meta.</summary>
public sealed record PagedResult(
    object Data, long Total, int Limit, int Offset,
    IReadOnlyDictionary<string, IReadOnlyList<FacetBucket>>? Facets = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? Aggregate = null);

public static class Envelope
{
    public static SuccessEnvelope Success(object? data, MetaInfo? meta = null) => new(true, data, meta);

    public static VersionedSuccessEnvelope Versioned(object? data, long version) =>
        new(true, data, new VersionMeta(version));

    public static ErrorEnvelope Error(
        string code, string message, IReadOnlyList<ValidationDetail>? details = null, string? traceId = null) =>
        new(false, new ErrorBody(code, message, details, traceId));
}

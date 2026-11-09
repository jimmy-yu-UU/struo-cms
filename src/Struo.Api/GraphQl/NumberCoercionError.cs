// src/Struo.Api/GraphQl/NumberCoercionError.cs
using HotChocolate;
using HotChocolate.Language;
using Struo.Api.Http;

namespace Struo.Api.GraphQl;

/// <summary>
/// Classifies the errors HotChocolate raises when an Int, Long or Float input cannot be coerced, so
/// <see cref="StruoErrorFilter"/> (variables and out-of-range literals) and
/// <see cref="TraceIdResultMiddleware"/> (document-validation errors, which never reach the filter)
/// answer them with the same code and message.
/// </summary>
internal static class NumberCoercionError
{
    private const string VersionTarget = "version";
    private const string ScalarInt = "Int";
    private const string ScalarLong = "Long";
    private const string ScalarFloat = "Float";

    // HotChocolate's Int and Long scalars throw, instead of reporting a coercion error, for an integer
    // outside their range (InvalidFormatException from a literal, FormatException from a variable), and
    // report a value of the wrong kind ("abc", true, 1.5) as an error with no exception at all. Only an
    // error HotChocolate itself raised while coercing an Int, Long or Float input counts, so a
    // FormatException thrown inside a resolver stays an unhandled error. With an exception the error
    // carries `inputPath` and `fieldType`; without one a variable carries the same pair and a literal
    // carries `fieldName` or `argument` with the `fieldType` or `locationType` it was checked against.
    // A literal names its target (the `coordinate`, `fieldName` or `argument`); one named `version` is
    // INVALID_VERSION, as on REST. A variable is named by the client, so its target is unknown and it is
    // BAD_USER_INPUT.
    internal static (string Code, string Message)? Classify(IError error, Exception? exception)
    {
        if (exception is not null and not (InvalidFormatException or FormatException)) return null;
        var extensions = error.Extensions;
        if (extensions is null) return null;
        var scalar = ScalarOf(extensions);
        if (scalar is not (ScalarInt or ScalarLong or ScalarFloat)) return null;

        var hasInputPath = extensions.ContainsKey("inputPath");
        var literalTarget = LiteralTarget(extensions, exception is not null);
        var isCoercionError = exception is not null
            ? hasInputPath
            : hasInputPath || literalTarget is not null;
        if (!isCoercionError) return null;

        var isVersion = literalTarget == VersionTarget;
        var code = isVersion ? Http.ErrorCodes.InvalidVersion : Http.ErrorCodes.BadUserInput;
        return (code, CoercionMessage(scalar, isVersion));
    }

    private static string? ScalarOf(IReadOnlyDictionary<string, object?> extensions)
    {
        if (!extensions.TryGetValue("fieldType", out var raw)
            && !extensions.TryGetValue("locationType", out raw))
            return null;
        return raw?.ToString()?.Trim('[', ']', '!');
    }

    private static string? LiteralTarget(IReadOnlyDictionary<string, object?> extensions, bool hasException)
    {
        if (hasException)
        {
            return extensions.ContainsKey("coordinate") && extensions.TryGetValue("inputPath", out var inputPath)
                ? inputPath?.ToString()?.Split('.')[^1]
                : null;
        }

        if (!extensions.TryGetValue("specifiedBy", out var spec)
            || spec?.ToString()?.Contains("Values-of-Correct-Type", StringComparison.Ordinal) != true)
            return null;
        if (extensions.TryGetValue("fieldName", out var fieldName)) return fieldName?.ToString();
        return extensions.TryGetValue("argument", out var argument) ? argument?.ToString() : null;
    }

    private static string CoercionMessage(string scalar, bool isVersion) => scalar switch
    {
        ScalarFloat => "The value must be a number.",
        ScalarInt => "The value must be an integer within the range of a 32-bit integer.",
        _ => isVersion
            ? "version must be an integer within the range of a 64-bit integer."
            : "The value must be an integer within the range of a 64-bit integer.",
    };
}

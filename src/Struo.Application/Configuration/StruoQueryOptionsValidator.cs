using Microsoft.Extensions.Options;

namespace Struo.Application.Configuration;

/// <summary>Cross-key rules for <see cref="StruoQueryOptions"/> that per-property attributes cannot express.</summary>
public sealed class StruoQueryOptionsValidator : IValidateOptions<StruoQueryOptions>
{
    public ValidateOptionsResult Validate(string? name, StruoQueryOptions options) =>
        options.DefaultLimit > options.MaxLimit
            ? ValidateOptionsResult.Fail(
                $"Query:DefaultLimit ({options.DefaultLimit}) must not exceed Query:MaxLimit ({options.MaxLimit}).")
            : ValidateOptionsResult.Success;
}

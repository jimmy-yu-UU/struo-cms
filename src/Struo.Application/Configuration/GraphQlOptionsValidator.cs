using Microsoft.Extensions.Options;

namespace Struo.Application.Configuration;

/// <summary>Rejects a GraphQL depth, cost or field-repetition limit outside its range at startup, naming the offending key.</summary>
public sealed class GraphQlOptionsValidator : IValidateOptions<GraphQlOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphQlOptions options)
    {
        List<string> failures = [];
        if (options.MaxExecutionDepth <= 0)
            failures.Add($"GraphQl:MaxExecutionDepth ({options.MaxExecutionDepth}) must be greater than 0.");
        if (options.MaxFieldCost is not > 0)
            failures.Add($"GraphQl:MaxFieldCost ({options.MaxFieldCost}) must be greater than 0.");
        if (options.MaxTypeCost is not > 0)
            failures.Add($"GraphQl:MaxTypeCost ({options.MaxTypeCost}) must be greater than 0.");
        if (options.MaxFieldCycleDepth is < 1 or > ushort.MaxValue)
            failures.Add($"GraphQl:MaxFieldCycleDepth ({options.MaxFieldCycleDepth}) must be between 1 and {ushort.MaxValue}.");
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

using Microsoft.Extensions.Options;

namespace Struo.Application.Configuration;

/// <summary>Rejects a non-positive GraphQL depth or cost limit at startup, naming the offending key.</summary>
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
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

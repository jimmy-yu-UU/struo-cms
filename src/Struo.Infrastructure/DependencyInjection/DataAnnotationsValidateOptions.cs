using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Struo.Infrastructure.DependencyInjection;

/// <summary>
/// Startup validation of an options object's <see cref="System.ComponentModel.DataAnnotations"/>
/// attributes (<c>[Required]</c>, <c>[Range]</c>, …) using the BCL <see cref="Validator"/>.
///
/// This is the same enforcement <c>OptionsBuilder.ValidateDataAnnotations()</c> provides, but
/// re-implemented on the BCL (<c>System.ComponentModel.DataAnnotations</c>, part of the runtime) so
/// it works inside the Struo.Infrastructure class library WITHOUT adding the
/// <c>Microsoft.Extensions.Options.DataAnnotations</c> package (that assembly ships only in the
/// ASP.NET shared framework, which this non-Web SDK library does not reference — ARC-5, zero new
/// packages). Register once per options type alongside <c>.ValidateOnStart()</c>.
/// </summary>
internal sealed class DataAnnotationsValidateOptions<TOptions> : IValidateOptions<TOptions>
    where TOptions : class
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(options, context, results, validateAllProperties: true))
            return ValidateOptionsResult.Success;

        var typeName = typeof(TOptions).Name;
        var failures = results
            .Where(r => !string.IsNullOrEmpty(r.ErrorMessage))
            .Select(r => $"DataAnnotation validation failed for '{typeName}': {r.ErrorMessage}")
            .ToList();

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Fail($"DataAnnotation validation failed for '{typeName}'.");
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Files;
using Struo.Infrastructure.Files;

namespace Struo.Infrastructure.DependencyInjection;

public static class FileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddStruoFiles(this IServiceCollection services)
    {
        // Bind via configuration (IOptions pattern, mirroring AddStruoData) so the FINAL merged
        // configuration is used — config sources added after this call (e.g. an integration-test
        // in-memory override) still take effect, and the backend is chosen from the resolved options.
        //
        // ARC-5: enforcement of FileStorageOptions.Validate() moves from "first resolve" to startup
        // (ValidateOnStart). The custom validator below wraps the existing Validate() so its precise
        // messages (RootPath / S3 credential / unknown-backend) are preserved.
        services.AddOptions<FileStorageOptions>()
            .BindConfiguration(FileStorageOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FileStorageOptions>, FileStorageOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<FileStorageOptions>>().Value);

        services.AddSingleton<IFileStorage>(sp =>
        {
            var options = sp.GetRequiredService<FileStorageOptions>();
            return string.Equals(options.Backend, "s3", StringComparison.OrdinalIgnoreCase)
                ? new S3FileStorage(options)
                : new LocalFileStorage(options);
        });

        services.AddSingleton<IImageDimensionReader, ImageDimensionReader>();
        services.AddScoped<FileService>();
        return services;
    }
}

/// <summary>Adapts <see cref="FileStorageOptions.Validate"/> to the options-validation pipeline so
/// its enforcement runs at startup (via <c>ValidateOnStart</c>) while preserving its precise failure
/// messages (ARC-5).</summary>
internal sealed class FileStorageOptionsValidator : IValidateOptions<FileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, FileStorageOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}

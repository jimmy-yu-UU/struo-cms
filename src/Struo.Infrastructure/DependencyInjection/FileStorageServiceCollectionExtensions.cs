using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        // Enforcement of FileStorageOptions.Validate() moves from "first resolve" to startup
        // (ValidateOnStart). The custom validator below wraps the existing Validate() so its precise
        // messages (RootPath / S3 credential / unknown-backend) are preserved.
        services.AddOptions<FileStorageOptions>()
            .BindConfiguration(FileStorageOptions.SectionName)
            .ValidateOnStart();
        // IPostConfigureOptions runs after all binding and before IValidateOptions (confirmed by
        // experiment: FileStorageOptionsValidator observes ImageTransform.AllowedFormats already
        // repopulated by this callback even when the configuration omits the key entirely), so
        // FileStorageOptionsValidator below still sees a fully-populated object.
        services.PostConfigure<FileStorageOptions>(options => options.ApplyCollectionDefaults());
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

        // Parameter object grouping the three storage-side collaborators FileService needs (S107:
        // keeps its constructor within the parameter-count guideline).
        services.AddSingleton(sp => new FileStorageServices(
            sp.GetRequiredService<IFileStorage>(), sp.GetRequiredService<IImageDimensionReader>(), sp.GetRequiredService<FileStorageOptions>()));

        // On-the-fly image transform (endpoint wiring) + its disk-backed variant cache.
        // NetVipsImageTransformer is stateless (no fields) so a Singleton is safe and avoids a
        // per-request allocation. DiskImageVariantCache is also stateless beyond its root path.
        services.AddSingleton<IImageTransformer, NetVipsImageTransformer>();
        services.AddSingleton<IImageVariantCache>(sp =>
        {
            var options = sp.GetRequiredService<FileStorageOptions>();
            var cachePath = options.ImageTransform.CachePath;

            // Relative CachePath must resolve against the app's content root, never the process CWD
            // (see FileStorageOptions.ImageTransformOptions.CachePath doc — a recurring prod footgun
            // in this codebase when a relative path is resolved against the launch directory instead).
            var env = sp.GetRequiredService<IHostEnvironment>();
            var root = Path.IsPathRooted(cachePath)
                ? cachePath
                : Path.Combine(env.ContentRootPath, cachePath);
            return new DiskImageVariantCache(root);
        });

        services.AddScoped<FileService>();
        return services;
    }
}

/// <summary>Adapts <see cref="FileStorageOptions.Validate"/> to the options-validation pipeline so
/// its enforcement runs at startup (via <c>ValidateOnStart</c>) while preserving its precise failure
/// messages.</summary>
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

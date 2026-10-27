using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Struo.Application.Query;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        // TryAdd so a fork's own IFileStorage wins whether it is registered before or after this call.
        // Backend=custom builds nothing: the fork must register one, and the factory below fails with a
        // message naming the key when none is registered (a later registration is the one resolved, so
        // this factory never runs then).
        services.TryAddSingleton<IFileStorage>(sp =>
        {
            var options = sp.GetRequiredService<FileStorageOptions>();
            if (string.Equals(options.Backend, "custom", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Struo:Files:Backend is 'custom' but no IFileStorage is registered. " +
                    "Register your implementation after AddStruoCms(), or set Struo:Files:Backend to 'local' or 's3'.");
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
        // libvips loaders flagged untrusted (e.g. ImageMagick, SVG) never run on uploaded content.
        // Setting it loads native libvips here, even when ImageTransform is disabled, so a host
        // without a usable libvips fails at startup instead of on the first transform.
        NetVips.NetVips.BlockUntrusted = true;
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
            return new DiskImageVariantCache(
                root, options.ImageTransform.CacheMaxBytes, sp.GetRequiredService<ILogger<DiskImageVariantCache>>());
        });
        // One gate for the whole process: the concurrency cap spans all requests.
        services.AddSingleton<ImageTransformGate>();
        services.AddSingleton<ImageTransformServices>();

        // FileService's trash and purge need the shared reference-handling pipeline; AddStruoData
        // registers the same type, so whichever runs first wins.
        services.TryAddScoped<ItemPurgePipeline>();
        services.AddScoped<FileServiceCollaborators>();
        services.AddScoped<FileService>();
        services.AddScoped<IFileLifecycle>(sp => sp.GetRequiredService<FileService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICollectionDeleteHandler, FileDeleteHandler>());
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

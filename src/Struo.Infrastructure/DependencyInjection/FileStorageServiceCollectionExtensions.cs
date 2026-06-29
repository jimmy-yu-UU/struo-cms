using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Files;
using Struo.Infrastructure.Files;

namespace Struo.Infrastructure.DependencyInjection;

public static class FileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddStruoFiles(this IServiceCollection services, IConfiguration config)
    {
        // Bind at resolve-time (IOptions pattern, mirroring AddStruoData) so the FINAL merged
        // configuration is used — config sources added after this call (e.g. an integration-test
        // in-memory override) still take effect, and the backend is chosen from the resolved options.
        services.Configure<FileStorageOptions>(config.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FileStorageOptions>>().Value;
            options.Validate(); // fail-fast on first resolve (startup)
            return options;
        });

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

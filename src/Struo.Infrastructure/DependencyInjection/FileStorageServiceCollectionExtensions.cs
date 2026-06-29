using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Files;
using Struo.Infrastructure.Files;

namespace Struo.Infrastructure.DependencyInjection;

public static class FileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddStruoFiles(this IServiceCollection services, IConfiguration config)
    {
        var options = new FileStorageOptions();
        config.GetSection(FileStorageOptions.SectionName).Bind(options);
        options.Validate(); // fail-fast at startup
        services.AddSingleton(options);

        if (string.Equals(options.Backend, "s3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, S3FileStorage>();
        else
            services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddSingleton<IImageDimensionReader, ImageDimensionReader>();
        services.AddScoped<FileService>();
        return services;
    }
}

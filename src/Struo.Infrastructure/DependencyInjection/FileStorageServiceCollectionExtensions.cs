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

        // Task 9 swaps in S3FileStorage when Backend=s3.
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddSingleton<IImageDimensionReader, ImageDimensionReader>();
        // Uncommented when FileService lands (Task 5):
        // services.AddScoped<FileService>();
        return services;
    }
}

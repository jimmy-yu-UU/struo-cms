using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

/// <summary>The three storage-side collaborators FileService needs, grouped so its constructor stays
/// within the parameter-count guideline; registered as a singleton by AddStruoFiles.</summary>
public sealed record FileStorageServices(IFileStorage Storage, IImageDimensionReader Images, FileStorageOptions Options);

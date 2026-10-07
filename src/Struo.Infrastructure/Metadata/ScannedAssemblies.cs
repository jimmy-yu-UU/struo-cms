using System.Reflection;

namespace Struo.Infrastructure.Metadata;

/// <summary>The framework assembly plus every caller assembly passed to <c>AddStruoMetadata</c>.</summary>
public sealed record ScannedAssemblies(IReadOnlyList<Assembly> All);

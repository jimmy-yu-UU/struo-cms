namespace Struo.Application.Security;

/// <summary>The framework's identity collection. Named here (mirroring <see
/// cref="Struo.Application.Files.FileCollection"/>) so <c>UserWriteInterceptor</c> and the Api layer
/// can recognize it without a bare string literal.</summary>
public static class UserCollection
{
    public const string Name = "user";
}

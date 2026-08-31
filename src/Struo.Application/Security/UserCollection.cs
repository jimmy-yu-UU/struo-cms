namespace Struo.Application.Security;

/// <summary>The framework's identity collection. Named here (mirroring <see
/// cref="Struo.Application.Files.FileCollection"/>) so the generic delete path in <c>ItemService</c>
/// can recognize it without a bare string literal.</summary>
public static class UserCollection
{
    public const string Name = "user";
}

using Struo.Application.Abstractions;

namespace Struo.Infrastructure.Identity;

/// <summary>Phase 0 placeholder. Replaced by a real session-backed accessor in Phase 6.</summary>
public sealed class StubCurrentUserAccessor : ICurrentUserAccessor
{
    public string? GetCurrentUserId() => "system";
}

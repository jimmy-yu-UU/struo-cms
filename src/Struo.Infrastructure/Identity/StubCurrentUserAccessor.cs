using Struo.Application.Abstractions;

namespace Struo.Infrastructure.Identity;

/// <summary>Phase 0/5.5 placeholder. Returns the nil UUID (system/seed actor). Replaced by a
/// real session-backed accessor in Phase 6.</summary>
public sealed class StubCurrentUserAccessor : ICurrentUserAccessor
{
    public Guid? GetCurrentUserId() => Guid.Empty;
}

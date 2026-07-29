using Struo.Application.Abstractions;

namespace Struo.Infrastructure.Identity;

/// <summary>Placeholder registered by default. Returns the nil UUID (system/seed actor). Replaced by a
/// real session-backed accessor when auth is wired up (see <c>AuthWiring.AddStruoAuth</c>).</summary>
public sealed class StubCurrentUserAccessor : ICurrentUserAccessor
{
    public Guid? GetCurrentUserId() => Guid.Empty;
}

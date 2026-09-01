using Struo.Application.Security;

namespace Struo.Tests.Support;

/// <summary>No-op stand-in for the many <c>ItemService</c> unit-test harnesses that don't exercise the
/// user-delete revocation path at all — keeps their constructor calls short and honest about not
/// caring about this dependency.</summary>
public sealed class NoopUserSessionRevocationService : IUserSessionRevocationService
{
    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
}

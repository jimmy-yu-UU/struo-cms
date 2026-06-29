using Struo.Application.Abstractions;

namespace Struo.Tests.Support;

public sealed class TestCurrentUserAccessor(Guid? userId) : ICurrentUserAccessor
{
    public Guid? GetCurrentUserId() => userId;
}

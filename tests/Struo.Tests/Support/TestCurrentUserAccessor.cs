using Struo.Application.Abstractions;

namespace Struo.Tests.Support;

public sealed class TestCurrentUserAccessor(string? userId) : ICurrentUserAccessor
{
    public string? GetCurrentUserId() => userId;
}

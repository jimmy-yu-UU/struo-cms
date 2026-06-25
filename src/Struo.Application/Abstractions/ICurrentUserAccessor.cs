namespace Struo.Application.Abstractions;

public interface ICurrentUserAccessor
{
    string? GetCurrentUserId();
}

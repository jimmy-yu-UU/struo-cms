namespace Struo.Application.Abstractions;

public interface ICurrentUserAccessor
{
    Guid? GetCurrentUserId();
}

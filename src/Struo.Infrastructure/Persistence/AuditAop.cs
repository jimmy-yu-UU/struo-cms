using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Domain.Auditing;

namespace Struo.Infrastructure.Persistence;

public static class AuditAop
{
    public static void Register(ISqlSugarClient client, ICurrentUserAccessor currentUser)
    {
        client.Aop.DataExecuting = (oldValue, entityInfo) =>
        {
            if (entityInfo.EntityValue is not IAuditable) return;

            var now = DateTime.UtcNow;
            var user = currentUser.GetCurrentUserId();

            if (entityInfo.OperationType == DataFilterType.InsertByObject)
            {
                switch (entityInfo.PropertyName)
                {
                    case nameof(IAuditable.CreatedAt):
                    case nameof(IAuditable.UpdatedAt):
                        entityInfo.SetValue(now);
                        break;
                    case nameof(IAuditable.CreatedBy):
                    case nameof(IAuditable.UpdatedBy):
                        entityInfo.SetValue(user);
                        break;
                }
            }
            else if (entityInfo.OperationType == DataFilterType.UpdateByObject)
            {
                switch (entityInfo.PropertyName)
                {
                    case nameof(IAuditable.UpdatedAt):
                        entityInfo.SetValue(now);
                        break;
                    case nameof(IAuditable.UpdatedBy):
                        entityInfo.SetValue(user);
                        break;
                }
            }
        };
    }
}

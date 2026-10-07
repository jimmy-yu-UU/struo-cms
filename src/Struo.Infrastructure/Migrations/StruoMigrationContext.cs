using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations;

/// <summary>Per-run facts a migration may read: the active backend and the framework table prefix.</summary>
public sealed record StruoMigrationContext(StruoDbType DbType, string TablePrefix);

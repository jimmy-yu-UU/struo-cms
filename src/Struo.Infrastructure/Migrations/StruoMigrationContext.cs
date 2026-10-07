using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations;

/// <summary>Per-run facts a migration may read: the active backend, the framework table prefix and the core seed inputs.</summary>
public sealed record StruoMigrationContext(StruoDbType DbType, string TablePrefix, CoreSeedData? Seed = null);

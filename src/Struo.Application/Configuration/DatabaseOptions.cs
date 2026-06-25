namespace Struo.Application.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public StruoDbType DbType { get; set; } = StruoDbType.PostgreSQL;
    public string ConnectionString { get; set; } = string.Empty;
}

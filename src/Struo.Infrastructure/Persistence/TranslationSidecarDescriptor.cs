namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Identifies one translation sidecar table's UNIQUE (foreign key, locale) index for
/// <see cref="SchemaGuard"/> to verify. Built by the caller — Program.cs derives one of these
/// per collection from <c>IMetadataProvider.GetCollections()</c>'s <c>Translation</c> metadata, resolving
/// the CLR type/property names to physical table/column names the same way SqlSugar does
/// (<c>ISqlSugarClient.EntityMaintenance.GetTableName</c>/<c>GetDbColumnName</c>) — so <see cref="SchemaGuard"/>
/// itself never needs to know about metadata, DI, or any specific collection, and a fork's own
/// sidecars are covered automatically, the same way core's <c>file_translations</c> is.
/// </summary>
public sealed record TranslationSidecarDescriptor(
    string TableName, string ForeignKeyColumn, string LocaleColumn);

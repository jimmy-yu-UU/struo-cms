# SqlSugar's reflection cache is scoped per provider

## Decision

Every SqlSugar client that maps entities gets a `ProviderScopedReflectionCache` as its
`ConfigureExternalServices.ReflectionInoCacheService`. There are two construction sites:
`SqlSugarClientFactory.Create` and `MigrationHost`, which builds the client that reads the version
table. The cache prefixes every key with `"<DbType>:"` and stores the entry in SqlSugar's own process-wide
`ReflectionInoCacheService`. `MigrationLock` runs only Ado calls and keeps SqlSugar's default
cache.

## Why

SqlSugar keeps one reflection cache per process, and two of its key formats do not name the
provider (SqlSugarCore 5.1.4.221, decompiled):

- Entity info: `"GetEntityInfo" + type.GetHashCode() + type.FullName + ConfigId`
  (`EntityMaintenance._GetEntityInfo`). `EntityService` runs only for the client that resolves a
  type first. The `DataType` it sets for a `[ColumnShape]` property comes from `ColumnTypeMap.For`
  and is specific to the provider. A second provider whose ConfigId matches reads the first
  provider's literal: SQL Server gets `timestamptz`, and `SchemaChecker` reports "expected
  DateTime, database has datetimeoffset".
- Row binders: `"DataReaderToList." + type.FullName + column names + the first two letters of each
  reader field's CLR type name` (`DbBindAccessory.GetCacheKey`). `Int32` and `Int64` both shorten
  to `In`. The emitted binder depends on the provider's reader type names. A binder built on MySQL
  for a `bigint` COUNT fails on SQL Server's `int` COUNT with "Count绑定到FacetRow`1失败".

A deployed process usually talks to one provider. The test process talks to several, and a fork
may add a second database (a reporting copy, say), so the scope belongs to the product.

## Evidence

- `SqlSugarClientFactoryTests.Clients_for_two_providers_with_one_prefix_resolve_their_own_column_types`
  and `MigrationHostClientTests` run without a server.
- `CrossProviderStartupGateLiveTests` runs the startup gate on two fresh databases that share a
  prefix.
- `LiveRepositoryTests.Own_field_facet_counts_bind_on_both_backends_of_one_process` runs one facet
  query on two backends, in both orders.

## Unknowns

MySQL and MariaDB both use `DbType.MySql`, so they share one scope. No binder or column-type
difference between them has been observed. A divergence would need a scope finer than the DbType.

## Referenced from

- `src/Struo.Infrastructure/Persistence/ProviderScopedReflectionCache.cs`

# `users.accesstoken` has a plain index, not a unique one

## Decision

The `accesstoken` column of the `users` table carries `ix_{table}_accesstoken`, an index that is not
unique (`[SugarIndex(..., false)]` on `User`, and `Create.Index` in
`src/Struo.Infrastructure/Migrations/Core/202610080000_CreateCoreSchema.cs`). No migration adds a unique
constraint on it, and the column is nullable.

The column holds `AccessTokenHasher.Hash(token)`: the uppercase hex SHA-256 of a token made from 32
bytes of `RandomNumberGenerator`, base64url encoded. The plaintext token is shown once and never stored.
A user without a token has `NULL` in the column; `ClearAccessTokenAsync` and `SetPasswordAsync` write
`NULL` back.

## Why

- Two users receiving the same token needs a collision in 256 random bits, which is negligible. The
  lookup `FindByAccessTokenAsync` compares the hash with `==` and takes the first row.
- A unique index on a nullable column is a problem on SQL Server: it admits one `NULL`. Most users have
  no token, so a unique index would make the second user without a token fail to write. The failure
  would land on ordinary user creation, not on token issuance.

## Evidence

Read from the code on 2026-11-04: the token size and encoding in `src/Struo.Application/Security/AccessTokenHasher.cs`,
the hashed storage and the nullable column in `src/Struo.Infrastructure/Identity/User.cs`, and the
non-unique index in the migration named above.

Measured on 2026-11-04 against SQL Server 2022 (`mcr.microsoft.com/mssql/server:2022-latest`) in a
throwaway container, with SqlSugar 5.1.4.221 typed APIs only: `CodeFirst.InitTables` on a class whose
`string?` column (length 255, nullable) has a unique `SugarIndex`, then two `Insertable` calls with a
`NULL` value. The first insert wrote one row. The second threw `SqlException`: "Cannot insert duplicate
key row in object 'dbo.UniqueNullable' with unique index 'ux_uniquenullable_token'. The duplicate key
value is (<NULL>)." The table held one row afterwards.

## Unknowns

The other backends' handling of several `NULL` values under a unique index was not measured. A filtered
unique index that excludes `NULL` was not evaluated, nor whether the typed migration API can express it
on every backend.

## Referenced from

- `src/Struo.Application/Security/AccessTokenHasher.cs`

# Aggregate averages and sums widen integral operands on SQL Server only

## Decision

`ColumnSelectorFactory.AggregateProjection` builds `SqlFunc.AggregateAvg(x.P)` for an `Avg` slot.
When `AggregateQueries` runs on SQL Server it passes `sqlServerIntegerAggregates: true`, and an integral
operand (`int`, `long`, their nullable forms and the other integer widths) is wrapped in
`SqlFunc.ToDouble`, which renders `AVG(CAST([col] AS FLOAT))`. A `Sum` slot over an integral operand
narrower than 64 bits (`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, and their nullable forms) is
wrapped in `SqlFunc.ToInt64`, which renders `SUM(CAST([col] AS BIGINT))`. Every other backend
aggregates the column as declared. The flag is named `sqlServerIntegerAggregates`.
`AggregateValueNormalizer` returns every average as a `double` and every integral sum as a `long`.

## Why

SQL Server's `AVG` over an integer column returns an integer: the mean of 2 and 3 comes back as 2,
not 2.5. PostgreSQL, MySQL, MariaDB and SQLite do not divide integers as integers in `AVG`, so the
cast is limited to SQL Server. No backend truncates an average to an integer.

The precision of the mean follows each backend:

- MySQL and MariaDB return `DECIMAL` with `div_precision_increment` extra decimals (default 4), so
  the mean of 1, 2 and 2 comes back as 1.6667.
- PostgreSQL returns `numeric`, SQL Server `FLOAT` (after the cast) and SQLite `REAL`.

`AggregateValueNormalizer` then converts the result to `double`.

SQL Server's `SUM` over an `int` column returns `int` and fails with an arithmetic overflow once the
total passes 2^31 - 1. PostgreSQL returns `bigint`, MySQL and MariaDB `DECIMAL`, SQLite an integer,
none of which overflow there, so the `BIGINT` cast is limited to SQL Server. `MIN`, `MAX` and `COUNT`
are not cast.

The SQL Server cast has a cost: `FLOAT` holds integers exactly only up to 2^53. A `long` value above
2^53 is rounded before it is summed, so the average of such values loses precision.

## Evidence

- `FacetSqlShapeTests.SqlServer_average_casts_integral_operands_to_float_and_leaves_other_ops_alone`
  pins the rendered SQL without a server.
- `LiveRepositoryTests.Average_has_no_integer_division_over_int_long_decimal_and_nullable_columns`
  runs the same average over `int`, `long`, `decimal` and `int?` columns on every configured live
  backend;
  `FacetSqlShapeTests.Average_has_no_integer_division_over_int_long_decimal_and_nullable_columns`
  runs it on SQLite.
- `LiveRepositoryTests.Average_with_a_repeating_mean_is_within_a_ten_thousandth` averages 1, 2 and 2
  on every configured live backend and accepts 5/3 to within 1e-4;
  `FacetSqlShapeTests.Average_with_a_repeating_mean_is_within_a_ten_thousandth` runs it on SQLite.

- `AvgProbe` sums an `int` column holding `int.MaxValue` on two rows. The assertion that the sum is the
  `long` `2 * int.MaxValue` runs in `LiveRepositoryTests.Average_has_no_integer_division_over_int_long_decimal_and_nullable_columns`
  on every configured live backend (SQL Server fails with the overflow without the cast) and in the
  SQLite twin.
- `FacetSqlShapeTests.SqlServer_average_casts_integral_operands_to_float_and_leaves_other_ops_alone`
  pins `SUM(CAST(... AS BIGINT))` and uncast `MIN`/`MAX`;
  `FacetSqlShapeTests.Other_backends_sum_and_average_integral_operands_without_a_cast` pins that
  PostgreSQL and MySQL render no cast.

## Unknowns

No test averages values above 2^53 on SQL Server, so the size of the error there is not measured.
Oracle is not covered.

## Referenced from

- `src/Struo.Infrastructure/Query/ColumnSelectorFactory.cs`
- `AGENTS.md` ("Verification")

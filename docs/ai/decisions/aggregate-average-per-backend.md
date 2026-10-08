# Aggregate averages cast integral operands to FLOAT on SQL Server only

## Decision

`ColumnSelectorFactory.AggregateProjection` builds `SqlFunc.AggregateAvg(x.P)` for an `Avg` slot.
When `AggregateQueries` runs on SQL Server it passes `floatAverageOfIntegers: true`, and an integral
operand (`int`, `long`, their nullable forms and the other integer widths) is wrapped in
`SqlFunc.ToDouble`, which renders `AVG(CAST([col] AS FLOAT))`. Every other backend averages the
column as declared. `AggregateValueNormalizer` returns every average as a `double`.

## Why

SQL Server's `AVG` over an integer column returns an integer: the mean of 2 and 3 comes back as 2,
not 2.5. PostgreSQL, MySQL, MariaDB and SQLite return the exact mean for the same column, so the cast
is limited to SQL Server.

The cast has a cost: `FLOAT` holds integers exactly only up to 2^53. On SQL Server, a `long` value
above 2^53 is rounded before it is summed, so the average of such values loses precision. On the
other backends the database computes the mean exactly and the rounding happens once, when the result
is converted to `double`.

## Evidence

- `FacetSqlShapeTests.SqlServer_average_casts_integral_operands_to_float_and_leaves_other_ops_alone`
  pins the rendered SQL without a server.
- `LiveRepositoryTests.Average_is_exact_over_int_long_decimal_and_nullable_columns` runs the same
  average over `int`, `long`, `decimal` and `int?` columns on every configured live backend;
  `FacetSqlShapeTests.Average_is_exact_over_int_long_decimal_and_nullable_columns` runs it on SQLite.

## Unknowns

No test averages values above 2^53 on SQL Server, so the size of the error there is not measured.
Oracle is not covered.

## Referenced from

- `src/Struo.Infrastructure/Query/ColumnSelectorFactory.cs`

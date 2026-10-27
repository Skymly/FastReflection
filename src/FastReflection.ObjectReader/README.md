# Skymly.FastReflection.ObjectReader

An `IEnumerable` → `DbDataReader` adapter over [Skymly.FastReflection](https://github.com/Skymly/FastReflection) member accesses, covering the `SqlBulkCopy`/`DataTable.Load` path.

```csharp
using FastReflection.ObjectReader;

// One column per Member of T's public type accessor, in accessor order
// (OrdinalAttribute honored).
using var reader = ObjectReader.Create(rows);

// Or an explicit case-sensitive member list, in supplied order.
using var selected = ObjectReader.Create<Row>(rows, "Id", "Name");
// non-generic: ObjectReader.Create(elementType, rows, "Id", "Name")

var table = new DataTable();
table.Load(reader);            // SqlBulkCopy.WriteToServer(reader) works the same
```

- An unknown member name fails at `Create` with `MissingMemberException` — no silent `object` column.
- Column type is `Nullable`-unwrapped; `AllowDBNull` is reference-type-or-`Nullable<T>`; nulls read back as `DBNull.Value`.
- Full `DbDataReader` surface: `FieldCount`, `GetName`, `GetOrdinal`, `GetValue`/`GetValues`, typed `Get*`, `IsDBNull`, `GetFieldType`, `GetDataTypeName`, `Read`, `IsClosed`, `HasRows`, `Close`, `GetSchemaTable`, `GetEnumerator`, indexers.
- Obtained only via `Create` — no public constructor. Zero third-party dependencies; same TFM set as the core (`net462;netstandard2.0;net8.0;net10.0`).
- `DbColumn`/`GetColumnSchema` metadata is post-1.0; the reverse direction, bulk helpers, non-public columns and member probes are out of scope. See the [repository](https://github.com/Skymly/FastReflection) for the 1.0 plan.

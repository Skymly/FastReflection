# Skymly.FastReflection.ObjectReader

An `IEnumerable`→`DbDataReader` adapter over [Skymly.FastReflection](https://github.com/Skymly/FastReflection) member accesses, covering the `SqlBulkCopy`/`DataTable.Load` path.

Obtain a reader via `ObjectReader.Create<T>(source, params string[] members)` or `ObjectReader.Create(elementType, source, params string[] members)`; with no member names, all public members of the element type become columns. See the [repository](https://github.com/Skymly/FastReflection) for the 1.0 plan.

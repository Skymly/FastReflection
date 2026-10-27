# Changelog

All notable changes to this project are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.0.0] - Unreleased

### Added

- Bound calls on `TypeAccessor`/`ObjectAccessor`: `Invoke`/`TryInvoke`, by-name `GetValue`/`SetValue`/`TryGetValue`/`TrySetValue` (instance and static members, indexers by index arguments), `CreateNew(args, namedArgs)`/`TryCreateNew`, and `GetMemberNames()` — a metadata-only resolver with spec-leaning semantics (no user-defined conversions, no type inference, no const narrowing); bound invokers cached on the accessor cache.
- `ReflectionExtensions.CreateConverter(sourceType, targetType)` — bounded conversions; `InvalidCastException` on failure.
- `Member.GetAttribute` / `Member.GetAttribute<T>`; `OrdinalAttribute` ordering for `GetMembers()`.
- `Skymly.FastReflection.ObjectReader` satellite — `IEnumerable` → `DbDataReader` for the `SqlBulkCopy`/`DataTable.Load` path, with fail-fast column names.
- `Skymly.FastReflection.XmlDocs` satellite — XML documentation reads on `MemberInfo`/`Type`/`ParameterInfo`/`Assembly`, with `XmlDocsOptions`.
- `net10.0` target on every package; per-package READMEs on NuGet.
- AOT/trim annotations across all packages (`CreateInvoker<TDelegate>` is the only public `RequiresDynamicCode`; `Unreflect` is the AOT-safe exact bind), verified by a `PublishAot` smoke app.

### Changed

- **Contract fix (0.1 → 1.0): writing an unwritable `Member` now throws `InvalidOperationException` instead of `MissingMemberException`** — an existing Member that cannot be written is not missing (ADR-0004).
- `ObjectAccessor.Equals` is symmetric: an accessor equals another accessor iff their targets are equal, and never equals the raw target.
- `TypeAccessor`/`ObjectAccessor` constructors can no longer be called externally; `Create` is the only obtain path.
- Emit and reflection-fallback paths now share one exception contract; callee exceptions propagate unwrapped on both, and a null `in` slot is not written back (ADR-0004).
- All packages are strong-named with one committed key pair; `AssemblyVersion` is major-only while `FileVersion` carries the full version (ADR-0003).

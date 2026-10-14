# FastReflection — agent notes

## Commands

- Build: `dotnet build FastReflection.slnx` (slnx format, requires .NET 9+ SDK)
- Test (all legs): `dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj`
- Test single leg: append `-f net8.0` or `-f net472` (net472 leg consumes the `net462` library build)
- Benchmarks: `dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -- --job short`

## Structure

- `src/FastReflection/` — the library, multi-targeted `net462;netstandard2.0;net8.0`
  - `ReflectionExtensions.cs` — public API surface (CreateGetter/Setter/Invoker/Factory, Unreflect, net8 Span invokers)
  - `Emit/EmitHelper.cs` — DynamicMethod factory + IL micro-helpers + `DynamicCodeSupported` probe
  - `Emit/AccessorEmitter.cs` — IL emit for accessors/invokers/factories + reflection fallbacks
  - `TypeAccessor.cs`, `ObjectAccessor.cs`, `Member.cs` — name-indexed access layer
  - `Polyfills.cs` — `RequiresUnreferencedCode`/`RequiresDynamicCode` for downlevel TFMs

## Conventions / gotchas

- net462/netstandard2.0 have no `MethodInfo.CreateDelegate<T>()` — use `CreateDelegate(Type)` + cast.
- `RuntimeFeature` doesn't exist on net462/netstandard2.0 — dynamic-code probe is `#if`'d per TFM.
- Ref-returning property without setter is writable via getter + `stobj`; `ref readonly` detected via `InAttribute`/`IsReadOnlyAttribute` modreq.
- Struct member writes use `unbox` (managed pointer into the box) so writes persist — not `unbox.any`.
- Tests disable xunit parallelization assembly-wide because `FallbackTests` toggles the global `EmitHelper.ForceReflectionFallback` flag.
- netstandard2.0 needs `System.Reflection.Emit.ILGeneration` + `System.Reflection.Emit.Lightweight` packages; net462/net8.0 have them in-box.

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues in Skymly/FastReflection. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical roles, labels equal to their names (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

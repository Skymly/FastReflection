# FastReflection

Fast .NET reflection member access via compiled delegates (`DynamicMethod`) — with transparent fallback to plain reflection when dynamic code is unavailable (AOT/trimmed environments).

**Target frameworks**: `net462`, `netstandard2.0`, `net8.0`

## Quick start

```csharp
using FastReflection;
using System.Reflection;

// Layer 3 — unified untyped invokers (signature unknown at compile time)
MethodInfo add = typeof(Foo).GetMethod("Add")!;
FastInvoker invoker = add.CreateInvoker();
object? result = invoker(target, new object?[] { 2, 3 });   // ref/out written back into the array

// Layer 2 — open member accessors
Func<object?, object?> get = typeof(Foo).GetProperty("Value")!.CreateGetter();
Action<object?, object?> set = typeof(Foo).GetProperty("Value")!.CreateSetter();
set(target, 42);

// Layer 1 — exact-signature delegates when the signature is known statically
var typed = add.Unreflect<Func<Foo, int, int, int>>();
int sum = typed(foo, 2, 3);

// Type-level name-indexed access (fast-member style)
TypeAccessor accessor = TypeAccessor.Create(typeof(Foo));
accessor[target, "Value"] = 42;
int ord = accessor.TryGetOrdinal("Value", out var i) ? i : -1;
object? v = accessor.GetValue(target, ord);   // hot path: skips the name lookup
object? instance = accessor.CreateNew();      // parameterless ctor (works for private ctors with allowNonPublicAccessors)

// Per-instance wrapper
ObjectAccessor wrapped = ObjectAccessor.Create(target);
wrapped["Value"] = 42;
```

### Notable capabilities

- Non-public members/accessors via `DynamicMethod(skipVisibility)` — no `BindingFlags` juggling needed once you hold the `MemberInfo`
- `ref`/`out` parameters written back into the args array; `in` supported
- Write-through assignment to `ref`-returning properties without a setter (`ref readonly` rejected)
- Value-type member writes persist into the box (`unbox` + managed pointer, not `unbox.any` copy)
- `static` members: pass any/`null` target
- Cached, thread-safe `TypeAccessor` keyed by `(Type, allowNonPublicAccessors)`
- `net8.0` additionally exposes `CreateSpanInvoker` backed by BCL `MethodInvoker`/`ConstructorInvoker`

### Explicit non-goals

- NRT/XML-docs semantic enrichment — use [Namotion.Reflection](https://github.com/RicoSuter/Namotion.Reflection)
- `IDynamicMetaObjectProvider`/`dynamic` dispatch — that's [dynamitey](https://github.com/ekonbenefits/dynamitey) territory

## Benchmarks (BenchmarkDotNet ShortRun, local machine)

The payoff is largest on .NET Framework, where `MethodInfo.Invoke` has not yet
received the runtime-level speedups added in .NET 7+:

| Method | .NET Framework 4.8.1 | net8.0 |
|---|---:|---:|
| DirectCall | ~0.6 ns | ~0 ns |
| PropertyInfo.GetValue | 109.84 ns | 9.54 ns |
| **FastReflection getter** | **8.19 ns (13× faster)** | **5.37 ns** |
| TypeAccessor[name] | 35.95 ns | 13.56 ns |
| TypeAccessor[ordinal] | 7.01 ns | 6.03 ns |
| MethodInfo.Invoke | 216.34 ns | 18.58 ns |
| BCL MethodInvoker | n/a | 11.36 ns |
| **FastReflection invoker** | **6.39 ns (34× faster)** | **5.59 ns** |

(24 B allocated per reflection call = the boxed `int` return value, common to all paths.)

## Layout

```
src/FastReflection/             library (net462;netstandard2.0;net8.0)
tests/FastReflection.Tests/     xunit (net472 → net462 build; net6.0 → netstandard2.0; net8.0)
benchmarks/                     BenchmarkDotNet (net472;net8.0)
docs/research/                  design research notes
```

## Build & test

```bash
dotnet build FastReflection.slnx
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj            # all legs
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj -f net8.0  # one leg
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -f net8.0 -- --job short
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -f net472 -- --job short
```

## Releasing

CI is driven by [Nuke](https://nuke.build) (`build.cmd <Target>`: `Clean`/`Restore`/`Compile`/`Test`/`Pack`/`Publish`).

- `ci.yml` runs `Test` on every push/PR to `main` (all three test legs).
- `publish.yml` runs `Publish` on tags matching `v*.*.*` and pushes `Skymly.FastReflection` to nuget.org. Requires a repo secret named `NUGET_API_KEY`; the package version comes from `<Version>` in `src/FastReflection/FastReflection.csproj` — bump it before tagging.

```bash
git tag v0.1.0 && git push --tags   # triggers nuget.org publish
```
```

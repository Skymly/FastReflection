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

## Benchmarks (net8.0, BenchmarkDotNet ShortRun)

| Method | Mean | Allocated |
|---|---:|---:|
| DirectCall | ~0 ns | – |
| PropertyInfo.GetValue | 9.54 ns | 24 B |
| **FastReflection getter** | **5.37 ns** | 24 B |
| TypeAccessor[name] | 13.56 ns | 24 B |
| TypeAccessor[ordinal] | 6.03 ns | 24 B |
| MethodInfo.Invoke | 18.58 ns | 24 B |
| BCL MethodInvoker | 11.36 ns | 24 B |
| **FastReflection invoker** | **5.59 ns** | 24 B |

(24 B = the boxed `int` return value, common to all paths.)

## Layout

```
src/FastReflection/             library (net462;netstandard2.0;net8.0)
tests/FastReflection.Tests/     xunit (net472 → exercises net462 build; net8.0)
benchmarks/                     BenchmarkDotNet (net8.0)
docs/research/                  design research notes
```

## Build & test

```bash
dotnet build FastReflection.slnx
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj            # all legs
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj -f net8.0  # one leg
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -- --job short
```

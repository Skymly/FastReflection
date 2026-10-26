# FastReflection

Turns a property, field, method, or constructor already known at runtime into a reusable access. When dynamic code is available the hot path is a compiled delegate; when it is not, an access that ordinary reflection can complete still completes, and one it cannot complete fails. A bind with no adaptation is not an access.

A type accessor names the instance properties and fields of a closed type. This library does not include dynamic-object or DLR dispatch, user-defined or inferred conversions, member-path grammars or events by name, contextual metadata or nullability meaning (XML-doc reading ships in `Skymly.FastReflection.XmlDocs`), or compile-time generated access. Terms live in [CONTEXT.md](CONTEXT.md).

**Target frameworks**: `net462`, `netstandard2.0`, `net8.0`, `net10.0`

## Quick start

```csharp
using FastReflection;
using System.Reflection;

// An access over a method. ref/out are written back into the array; value types are boxed.
MethodInfo add = typeof(Foo).GetMethod("Add")!;
FastInvoker invoker = add.CreateInvoker();
object? result = invoker(target, new object?[] { 2, 3 });

// An access over a non-indexer property. A static access has no target.
Func<object?, object?> get = typeof(Foo).GetProperty("Value")!.CreateGetter();
Action<object?, object?> set = typeof(Foo).GetProperty("Value")!.CreateSetter();
set(target, 42);

// Not an access: exact signature, no adaptation, no fallback.
var typed = add.Unreflect<Func<Foo, int, int, int>>();
int sum = typed(foo, 2, 3);

// Ref stands in for one by-ref parameter. The access reads and writes Value in place.
delegate bool Parse(Foo target, string text, out Ref<int> value);
var parse = typeof(Foo).GetMethod("TryParse")!.CreateInvoker<Parse>();
parse(foo, "12", out var parsed); // parsed.Value == 12

// Type accessor: the Members of a closed type at one visibility. Names are case-sensitive.
TypeAccessor typeAccessor = TypeAccessor.Create(typeof(Foo));
typeAccessor[target, "Value"] = 42;
int ord = typeAccessor.TryGetOrdinal("Value", out var i) ? i : -1;
object? v = typeAccessor.GetValue(target, ord); // Ordinal is an index in this type accessor, not a name
object? instance = typeAccessor.CreateNew();    // not a Member; a value-type default is not an access

// Object accessor: the type accessor of the object's runtime type, bound to that object.
ObjectAccessor wrapped = ObjectAccessor.Create(target);
wrapped["Value"] = 42;
```

### Notable capabilities

- An access can be built over a non-public property, field, method, or constructor once you hold the reflection object. The public and non-public type accessors of the same closed type are different.
- An `object[]` or `Span<object?>` access writes `ref`/`out` back into the buffer and boxes value-type arguments and returns. A `Ref<T>` is not that buffer slot.
- A ref-returning property with no setter can still have a write access, unless it is `ref readonly`. That write does not make the property a writable Member.
- A write to a boxed value type updates that box. See [ADR 0001](docs/adr/0001-boxed-value-writes-update-the-box.md).
- A static access has no target. An instance access requires a non-null compatible target: the runtime type is the declaring type, a subtype, or an implementation, or the target is a box of that value type.
- Indexer properties are not Members, and they are not subjects of `CreateGetter` / `CreateSetter`. Call `get_Item` / `set_Item` through `CreateInvoker`.
- An open generic type has no type accessor. An open generic method must be constructed before an access can cover it.
- `CreateInvoker<TDelegate>()` binds a public exact signature with `CreateDelegate`; that bind is not an access. Adaptation, including `Ref<T>` and non-public methods `CreateDelegate` cannot bind, is an access and throws when dynamic code is unavailable.
- `net8.0+` `CreateSpanInvoker` is an access over the caller's `Span<object?>` buffer (a span of references cannot be `stackalloc`'d). It still boxes. When dynamic code is unavailable it falls back to BCL `MethodInvoker` / `ConstructorInvoker`.
- `CanWrite` means that type accessor can write the Member, including an init-only property after construction. It does not mean C# can assign it, and `required` is not enforced.
- A name identifies at most one Member. Which declaration wins is not promised. An ordinal is not an identity outside the type accessor you hold.

### Explicit non-goals

- `IDynamicMetaObjectProvider` / `dynamic` / DLR dispatch — that's [dynamitey](https://github.com/ekonbenefits/dynamitey) territory
- User-defined or inferred conversions — the bounded conversion set only
- Member paths and events by name — post-1.0
- NRT / contextual metadata — post-1.0 doc tooling; XML-doc reading ships in `Skymly.FastReflection.XmlDocs`
- Compile-time source-generated access — post-1.0 companion generator

## Benchmarks (BenchmarkDotNet ShortRun, local machine)

The payoff is largest on .NET Framework, where `MethodInfo.Invoke` has not yet
received the runtime-level speedups added in .NET 7+:

| Method | .NET Framework 4.8.1 | net8.0 |
|---|---:|---:|
| DirectCall | ~0.6 ns | ~0 ns |
| PropertyInfo.GetValue | 109.84 ns | 9.54 ns |
| **CreateGetter** | **8.19 ns (13× faster)** | **5.37 ns** |
| Type accessor by name | 35.95 ns | 13.56 ns |
| Type accessor by ordinal | 7.01 ns | 6.03 ns |
| MethodInfo.Invoke | 216.34 ns | 18.58 ns |
| BCL MethodInvoker | n/a | 11.36 ns |
| **CreateInvoker** | **6.39 ns (34× faster)** | **5.59 ns** |

(24 B allocated per reflection call = the boxed `int` return value, common to all paths.)

## Layout

```
CONTEXT.md                      domain glossary
docs/adr/                       decision records
src/FastReflection/             core library (net462;netstandard2.0;net8.0;net10.0)
src/FastReflection.ObjectReader/  satellite: IEnumerable → DbDataReader (same TFM set)
src/FastReflection.XmlDocs/       satellite: XML-docs reads (same TFM set)
tests/FastReflection.Tests/     xunit (net472 → net462 build; net6.0 → netstandard2.0; net8.0; net10.0)
tests/FastReflection.AotSmoke/  PublishAot smoke app (net8.0;net10.0)
benchmarks/                     BenchmarkDotNet (net472;net8.0;net10.0)
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

- `ci.yml` runs `Test` on every push/PR to `main` (all four test legs on windows; the non-framework legs plus the linux-x64 AOT smoke on ubuntu).
- `publish.yml` runs `Publish` on tags matching `v*.*.*` and pushes the three `Skymly.FastReflection*` packages to nuget.org. Requires a repo secret named `NUGET_API_KEY`; the lockstep package version comes from `<Version>` in `Directory.Build.props` — bump it before tagging.

```bash
git tag v0.1.0 && git push --tags   # triggers nuget.org publish
```

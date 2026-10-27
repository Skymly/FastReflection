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

## Bound calls

A bound call resolves a case-sensitive name plus an argument signature once, then runs as an adapted invoker through the same machinery as an access — including the reflection fallback. It is not an access and not a Member: the domain adds methods, indexers, statics, and constructors to the instance-Member domain.

```csharp
TypeAccessor accessor = TypeAccessor.Create(typeof(Foo));

accessor.Invoke(target, "Add", 2, 3);                       // method by name; overloads resolved
accessor.SetValue(target, "Value", 42);                     // property/field by name
object? item = accessor.GetValue(target, "Item", 0);        // indexer binds under its item name
object created = accessor.CreateNew(new object?[] { "x" }); // constructor by arguments

// Static members bind the same way and ignore target.
accessor.Invoke(null, "GetDefault", Array.Empty<object?>());

// Named arguments reorder by parameter name; generics take explicit type arguments.
accessor.Invoke(target, "Configure", args, namedArgs, typeArguments: new[] { typeof(int) });

// Every verb has a Try* twin: false on a name miss or failed bind, nothing else swallowed.
accessor.TryInvoke(target, "Add", args, out object? result);

// The distinct bindable names: methods, properties, fields, and the indexer item name.
IReadOnlyList<string> names = accessor.GetMemberNames();

// A bounded converter — not a name lookup. Failure is InvalidCastException.
Func<object?, object?> convert = ReflectionExtensions.CreateConverter(typeof(int), typeof(long));
```

- Argument conversions are the sanctioned set only: identity, reference assignment, unboxing, `Nullable<T>`, enum↔underlying, and C#-spec primitive widening (including `→decimal`, excluding `byte→char`); `null` applies to nullable targets only. User-defined conversions, generic type inference, const narrowing, and extension-method search never apply.
- `params` binds in normal or expanded form; optional parameters fill; a candidate needing no substitution beats one that fills. Named-argument matching is case-sensitive.
- Failure order, contractually ([ADR-0004](docs/adr/0004-one-exception-contract-for-emit-and-reflection-completion.md)): null `name` → `ArgumentNullException`; absent name → `MissingMemberException`; no applicable candidate → `MissingMethodException`; best-match tie → `AmbiguousMatchException`; null `args` → `ArgumentNullException`; target rules → `ArgumentNullException`/`InvalidCastException`; per-argument conversion → `InvalidCastException`; a callee exception propagates unwrapped. `Try*` returns `false` on the lookup stages only.
- Bound invokers are cached on the type accessor, keyed by member kind, name and normalized signature; there is no public flush, and growth is bounded only by call-site variety.

## Members and ordering

`GetMembers()` returns the `IReadOnlyList<Member>` of one type accessor — instance properties and fields, never methods, indexers, consts, or statics.

```csharp
foreach (Member m in TypeAccessor.Create(typeof(Foo)).GetMembers())
{
    string name = m.Name;          // case-sensitive
    Type type = m.Type;
    int ordinal = m.Ordinal;       // positional index in this accessor's list
    bool rw = m.CanRead && m.CanWrite;
    var attr = m.GetAttribute<DisplayAttribute>();
}
```

`OrdinalAttribute` (`property`/`field`, one `int` value) orders the list: attributed members sort first, ascending by value; unattributed members follow; equal values keep enumeration order. `Member.Ordinal` remains the positional index in the resulting list — the attribute is an ordering input, never a visible index.

## Notable capabilities

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

## Explicit non-goals

- `IDynamicMetaObjectProvider` / `dynamic` / DLR dispatch — that's [dynamitey](https://github.com/ekonbenefits/dynamitey) territory
- User-defined or inferred conversions — the bounded conversion set only
- Member paths and events by name — post-1.0
- NRT / contextual metadata — post-1.0 doc tooling; XML-doc reading ships in `Skymly.FastReflection.XmlDocs`
- Compile-time source-generated access — post-1.0 companion generator

## AOT and trimming

The packages build with `IsAotCompatible`, so trim, AOT and single-file findings (IL2xxx/IL3xxx) fail the build rather than a consumer's publish. The contract ([policy #9](https://github.com/Skymly/FastReflection/issues/9)):

- `CreateInvoker<TDelegate>` is the only public member carrying `RequiresDynamicCode` — the adapted bind needs it. `Unreflect<TDelegate>` is the AOT-safe exact bind for public signatures.
- `TypeAccessor.Create`, `ObjectAccessor.Create`, `CreateFactory(Type)` and the `TDelegate` parameter carry `DynamicallyAccessedMembers` union sets, so trimming keeps the members the accessor domain can discover.
- When dynamic code is unavailable, an access ordinary reflection can complete still completes (the span invokers fall back to BCL `MethodInvoker`/`ConstructorInvoker`); an adapted `Ref<T>` bind throws `NotSupportedException`.
- A `PublishAot` smoke app asserts the contract end-to-end on linux-x64 in CI. The satellites annotate the same way.

The static accessor cache keys `Type` strongly, which pins collectible `AssemblyLoadContext`s — the core makes no unload promise (probe-verified in the [cache research](https://github.com/Skymly/FastReflection/blob/827160a58d0648e69532b56ac408679f0b4c2c24/docs/research/2026-10-24-cache-alc-unload.md)). Satellite caches use `ConditionalWeakTable` and release with their assembly.

## Migrating from fast-member

The core covers fast-member's used surface one-for-one; `ObjectReader` moved to the `Skymly.FastReflection.ObjectReader` package.

| FastMember | Skymly.FastReflection |
|---|---|
| `TypeAccessor.Create(type)` | `TypeAccessor.Create(type)` — identical, including `allowNonPublicAccessors` |
| `accessor[target, "Name"]` get/set | same indexer — a name miss is `MissingMemberException`; writing an unwritable Member is `InvalidOperationException` (**deviation**) |
| `ObjectAccessor.Create(obj)`, `wrapped["Name"]` | same — `Equals` is symmetric: an accessor never equals the raw target |
| `CreateNew()`, `CreateNewSupported` | same; plus `CreateNew(args, namedArgs)` / `TryCreateNew` for bound construction |
| `GetMembers()` → `MemberSet` | `GetMembers()` → `IReadOnlyList<Member>`; no `MemberSet`/`GetMembersSupported` — filter with LINQ |
| `Member.Name`/`.Type`/`.Ordinal`/`.CanRead`/`.CanWrite`/`.IsDefined`/`.GetAttribute` | same names |
| `[Ordinal(n)]` | `FastReflection.OrdinalAttribute` — **deviation**: `Member.Ordinal` is the *position* in the ordered list, not the attribute value; the attribute only sorts |
| `ObjectReader.Create(source, members)` | `Skymly.FastReflection.ObjectReader` package — **deviation**: an unknown member name fails at `Create` with `MissingMemberException` (no silent `object` column) |
| subclassing the accessors | wontfix — `Create` is the only obtain path |

There is no `FastMember` source-compatibility shim (wontfix) — port call sites per this table.

## Migrating from Dynamitey

Every production-used verb maps to a bound call on `TypeAccessor`/`ObjectAccessor` — same name-lookup domain, no DLR or `Microsoft.CSharp` dependency.

| Dynamitey (`Dynamic.*`) | Skymly.FastReflection |
|---|---|
| `InvokeGet` / `InvokeSet` | `GetValue` / `SetValue` by name (+ `TryGetValue` / `TrySetValue`) |
| `InvokeMember` / `InvokeMemberAction` | `Invoke` / `TryInvoke` (ignore the boxed return for actions) |
| `InvokeConstructor` | `CreateNew(args, namedArgs)` / `TryCreateNew` on the type accessor |
| `InvokeGetIndex` / `InvokeSetIndex` | `GetValue` / `SetValue` with `params object?[]? index` — indexers bind under their item name |
| `InvokeContext` / `StaticContext` (statics) | bound calls on `TypeAccessor`; a static member ignores `target` |
| named arguments / generic methods | `Invoke(…, namedArgs, typeArguments)` — named args reorder; type args are explicit (no inference) |
| `InvokeConvert` | `ReflectionExtensions.CreateConverter(sourceType, targetType)` — **deviation**: bounded conversions only; user-defined `op_Implicit`/`op_Explicit` are never consulted |
| `GetMemberNames` | `GetMemberNames()` on either accessor |
| `CacheableInvocation` / `CreateCallSite` | automatic — bound invokers are cached on the accessor cache |
| `Dynamic.Invoke` / `InvokeAction` on a held callable | wontfix — compose with `delegate.DynamicInvoke(…)` or `CreateInvoker` over its `Method` |
| `InvokeSetAll`, member chains, events by name, operator verbs, COM, `IDMOP` targets | wontfix / post-1.0 — see the [1.0 plan](docs/plans/1.0.md) |
| `Curry` / `Tupler` / `Builder` / `DynamicObjects` family / `Linq` / `ConvertAll` | wontfix |
| `IsDBNull`, `GenericDelegateType`, `FastDynamicInvoke`, `ClearCaches`, `ApplyEquivalentType` | wontfix — BCL-covered plumbing |

**Deviation: no `TargetInvocationException` on any path** — a callee exception propagates unwrapped on the emit and the reflection-fallback path alike (ADR-0004). Names are case-sensitive throughout.

## Migrating from Namotion.Reflection

XML documentation reads ship in the `Skymly.FastReflection.XmlDocs` satellite with the same method spellings, namespace `FastReflection.XmlDocs`.

| Namotion.Reflection | Skymly.FastReflection.XmlDocs |
|---|---|
| `GetXmlDocsSummary` / `GetXmlDocsRemarks` | identical names on `MemberInfo`/`Type` |
| `GetXmlDocsTag(name)` / `GetXmlDocsElement()` | identical; element is `XElement?` |
| `parameterInfo.GetXmlDocs()` | identical |
| `XmlDocsOptions` (`PathToXmlFile`, `ResolveExternalXmlDocs`) | same options type |
| `GetXmlDocsPath`, `ToXmlDocsContent`, `GetXmlDocsRecordPropertySummary` | wontfix |
| `XmlDocs.ClearCache` / `CachedType.ClearCache` | no public flush — the per-assembly cache releases with its `Assembly` |
| `Namotion.Reflection.Cecil` XML docs | wontfix |
| `ContextualType`/`CachedType`, `ToContextual*`/`GetContextual*`, attribute split, NRT `Nullability` | post-1.0 doc tooling |
| `HasProperty` / `TryGetPropertyValue` | wontfix — `TryGetOrdinal`/`TryGetValue` covers the capability |

## Migrating from DotNext.Reflection

| DotNext.Reflection | Skymly.FastReflection |
|---|---|
| `method.Unreflect<TDelegate>()` | `MethodInfo.Unreflect<TDelegate>()` — same exact-bind contract, public signatures only |
| `Unreflect()` → `DynamicInvoker` | `CreateInvoker()` → `FastInvoker`; ctor → `FastConstructorInvoker`; net8+ `CreateSpanInvoker` → `FastSpanInvoker`/`FastSpanConstructorInvoker` |
| `MemberGetter`/`MemberSetter` delegate conversions | `CreateGetter`/`CreateSetter` — open and `<TDeclaring, TValue>` typed forms |
| `Type<T>.Method`/`.Property`/`.Field`, `MemberCache`, `IMember` member objects | wontfix — `TypeAccessor.Create(typeof(T))` + `GetMembers()`/`TryGetOrdinal` + `Member.MemberInfo` covers lookup and cache |
| `Function`/`Procedure` argument packs, `Bind` | wontfix — `FastInvoker` and bound `Invoke` cover the call |
| `Field<T,TValue>` `ref` access | wontfix — no live-reference returns |
| `Dynamic.Invoke` / `InvokeAction` | wontfix — `Delegate.DynamicInvoke` / `CreateInvoker` composition |
| `Reflector.MemberOf`, `ExtensionRegistry`, `MethodExtensions.*`, `Devirtualize`, `DelegateType` | wontfix — BCL equivalents exist |

### Named deviations

The differences a migration must know about, in one place:

- `Member.Ordinal` is the positional index in the accessor's member list — fast-member exposes the `[Ordinal]` *attribute value* there.
- `CreateConverter` applies the bounded conversion set only — Dynamitey's `InvokeConvert` consults user-defined conversions; this one never does.
- No `TargetInvocationException` anywhere — callee exceptions propagate unwrapped on both the emit and the reflection-fallback path.
- `ObjectReader.Create` fails fast: an unknown member name throws `MissingMemberException` at create instead of yielding a silent `object` column.
- Writing an unwritable `Member` throws `InvalidOperationException`, not `MissingMemberException` (a 0.1 defect fixed for 1.0 — see [CHANGELOG](CHANGELOG.md)).
- No `FastMember` source-compatibility shim ships; this section is the shim.

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
CHANGELOG.md                    release notes (1.0.0 unreleased)
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

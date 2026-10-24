# Static caches of Type/MemberInfo/delegates vs unloading: collectible AssemblyLoadContext on net8.0, netstandard2.0-on-Core, and AppDomains on net462

Research note for GitHub issue Skymly/FastReflection#4 (feeds the cache unload gate and the package and API specs). Date: 2026-10-24. Evidence only; no decision is made or recommended here.

## Question

How do static caches that hold `Type`, `MemberInfo`, delegates or `DynamicMethod`s interact with unloading a collectible `AssemblyLoadContext` (net8.0, and the netstandard2.0 asset running on .NET Core) and an `AppDomain` (net462)? Which established patterns work on each target framework, and at what cost? Patterns under review: `ConditionalWeakTable` keyed by `Type`/`MemberInfo`, weak references, per-context caches, `DynamicMethod` collectibility, and `TypeBuilder` with `RunAndCollect`.

## Short answer

The design-council claim is confirmed, both by code reading and empirically: `TypeAccessor.s_cache` is a static `ConcurrentDictionary` whose key struct holds a strong `Type` ([TypeAccessor.cs L16-32]) and whose value holds the type, its `MemberInfo`s and emitted delegates ([L83-138]); calling the real `TypeAccessor.Create(t)` on a type from a collectible `AssemblyLoadContext` kept the ALC, assembly and type alive through 10 full GC cycles (probe C1). A `ConcurrentDictionary` pins whether the `Type` sits in the key (C2) or only in the value (C14). `ConditionalWeakTable` released in **every** tested case on net8.0 — including `value == key`, a lambda capturing the key, a delegate built on a `DynamicMethod` owned by the collectible type, a bound delegate to a collectible static method, and a `RuntimeTypeAccessor`-shaped value holding `Type`+`PropertyInfo`+delegates (C3-C12). On net462 the scenario inverts: assemblies never unload individually (only `AppDomain.Unload`, which is forced, not cooperative), `RuntimeType` objects are rooted for the domain's life even when uncached (probe F0/F1), and a `Type` marshals **by value** across domains — handing a child's `Type` to the default domain loads the plugin assembly *into the default domain* (F2). `TypeBuilder`+`RunAndCollect` produces genuinely collectible types on both net8.0 and net472 (R0, F4), with `NotSupportedException` when an ordinary dynamic type statically references a collectible one (R2, F6).

## Findings

### 1. Verdict on the TypeAccessor.cs claim (seats M and Q): confirmed

The cache ([src/FastReflection/TypeAccessor.cs](https://github.com/Skymly/FastReflection/blob/main/src/FastReflection/TypeAccessor.cs), worktree read 2026-10-24):

```csharp
private readonly struct CacheKey : IEquatable<CacheKey>
{
    internal readonly Type Type;            // L18 — strong Type reference in the key
    internal readonly bool NonPublic;
    ...
}
private static readonly ConcurrentDictionary<CacheKey, TypeAccessor> s_cache = new();  // L32
public static TypeAccessor Create(Type type, bool allowNonPublicAccessors = false)
{
    ...
    return s_cache.GetOrAdd(                // L44-46 — static root, default context
        new CacheKey(type, allowNonPublicAccessors),
        static k => new RuntimeTypeAccessor(k.Type, k.NonPublic));
}
```

The cached `RuntimeTypeAccessor` independently keeps the collectible context alive through four strong paths; any one suffices:

- `CacheKey.Type` (L18) and `RuntimeTypeAccessor._type` (L83, set L91) — the `Type` itself.
- `_members` (L86): each `Member` holds `MemberInfo` and `Type` ([Member.cs L8-13]); a `PropertyInfo`/`FieldInfo` reaches its declaring `RuntimeType`.
- `_entries` (L85): getter/setter delegates. The emit path creates `DynamicMethod`s **hosted by the collectible declaring type** — `EmitHelper.CreateDynamicMethod` emits `new DynamicMethod(name, returnType, parameterTypes, owner, skipVisibility: true)` ([EmitHelper.cs L46-47]) and every `AccessorEmitter.Emit*` factory passes `DeclaringType(...)` as `owner` (e.g. [AccessorEmitter.cs L33-44, L83-95, L133-151]) — so the method's code lives in the collectible context's loader allocator and the delegate keeps it alive.
- The reflection-fallback lambdas close over the collectible `MethodInfo`/`PropertyInfo`/`FieldInfo` ([TypeAccessor.cs L202, L210, L217, L226]), and `_factory` (L87) similarly.

Empirical confirmation: loading `Plugin.dll` into a collectible ALC, calling `TypeAccessor.Create(t)` (and `Create(t, true)`) from the default context, dropping all locals and calling `alc.Unload()` leaves `WeakReference`s to the ALC, the `Assembly` and the `Type` all `ALIVE` after 10 × (`GC.Collect` + `WaitForPendingFinalizers`); the no-cache control dies in the first pass (probe C1 vs C0, appendix).

### 2. The collectibility contract (what the docs require for unload)

- [.NET unloadability guide](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability) ([gitcommit 156931bb](https://github.com/dotnet/docs/blob/156931bb4ec1e81b028c76ea983553f2e9778bdd/docs/standard/assembly/unloadability.md)): `AssemblyLoadContext.Unload()` only *initiates* unload — it is **cooperative**, unlike `AppDomain` unload which is **forced** ("all threads running in the target AppDomain are aborted"). Unload finishes only when no outside strong references remain to the ALC's assemblies, types, or instances thereof — `WeakReference`/`WeakReference<T>` are the blessed exceptions — and no threads run the ALC's code.
- The same page's troubleshooting list names as pins: references in "a stack slot or a processor register (method locals…), **a static variable**, or a strong (pinning) GC handle" reaching the collectible types; threads running collectible code; noncollectible-ALC instances created inside the collectible ALC; pending `RegisteredWaitHandle`s; and fields on the custom `AssemblyLoadContext` subclass itself. The fixpoint loop (`GC.Collect`/`WaitForPendingFinalizers` until the `WeakReference` to the ALC dies) is the documented completion check.
- [Collectible assemblies for dynamic type generation](https://learn.microsoft.com/en-us/dotnet/fundamentals/reflection/collectible-assemblies) ([gitcommit 20dfcbac](https://github.com/dotnet/docs/blob/20dfcbac7f55f648d6c44e4add846557e2f81501/docs/fundamentals/reflection/collectible-assemblies.md)): a `RunAndCollect` assembly stays alive while *any* of these exist: an instance/`Type`/`TypeBuilder`/`MemberInfo`-reachable object of a defined type, a `RuntimeTypeHandle`/`RuntimeMethodHandle`, **"a delegate to a static method that's defined in a module of the assembly"**, a generic instance parameterized on `T` (even an empty `List<T>`), a method of the assembly on any call stack, or even an `ILGenerator`/`LocalBuilder` used during construction. Restrictions: ordinary (non-collectible) dynamic types cannot statically reference collectible types (`NotSupportedException` — reproduced, R2/F6); on .NET Framework additionally no COM, no `[DllImport]`, no `calli`, no marshaling of delegates, no `ContextBoundObject`, no thread-statics.

### 3. Empirical results (net8.0, .NET 8 SDK 8.0.426, Release build, one collectible ALC per case)

| # | Cache shape | ALC | Assembly | Type |
|---|---|---|---|---|
| C0 | control — nothing cached | dead | dead | dead |
| C1 | real `TypeAccessor.Create(t)` (+ nonpublic) | **ALIVE** | **ALIVE** | **ALIVE** |
| C2 | `ConcurrentDictionary<Type,object>`, value = `t.FullName` | **ALIVE** | **ALIVE** | **ALIVE** |
| C3 | `ConditionalWeakTable<Type,object>`, value independent | dead | dead | dead |
| C4 | `CWT<Type,Type>`, **value == key** | dead | dead | dead |
| C5 | `CWT`, value = `() => t.FullName` (closure captures key) | dead | dead | dead |
| C6 | `CWT`, value = delegate on `DynamicMethod` **owned by** the collectible type (`skipVisibility`, FastReflection shape) | dead | dead | dead |
| C7 | `CWT`, value = `o => get.Invoke(o, null)` closure over collectible `MethodInfo` | dead | dead | dead |
| C8 | `CWT<PropertyInfo,object>`, value independent | dead | dead | dead |
| C9 | per-context cache: `Type` stored in a static *inside* the collectible ALC | dead | dead | dead |
| C10 | `CWT`, value = delegate on **anonymously hosted** `DynamicMethod` whose IL calls the collectible getter | dead | dead | dead |
| C11 | `CWT`, value = delegate bound directly to a collectible static method | dead | dead | dead |
| C12 | `CWT`, value = accessor-like object holding `Type` + `PropertyInfo` + emitted delegates + factory closure | dead | dead | dead |
| C13 | `Dictionary<string,WeakReference>` | dead | dead | dead |
| C14 | `ConcurrentDictionary<string,object>`, string key, **value holds the `Type`** | **ALIVE** | **ALIVE** | **ALIVE** |
| R0 | `TypeBuilder` in `RunAndCollect` assembly, nothing cached | dead | dead | dead |
| R1 | `RunAndCollect` type cached in `ConcurrentDictionary` | —¹ | —¹ | **ALIVE** |
| R2 | ordinary dynamic type inheriting a collectible type | `NotSupportedException` (at `CreateType`) | | |

¹ In R1 both `WeakReference`s were to the `AssemblyBuilder` object, which itself collected; the rooted `Type` kept the underlying loader allocator (and hence the dynamic assembly's types) alive — a rooted `Type` is what pins a collectible assembly, matching the doc's lifetime list.

Reading of C4-C12: these match the documented ephemeron semantics. `ConditionalWeakTable` holds its entries as ephemerons — the *value* is kept alive only while the *key* is alive, and references **from** values **to** keys do not count. The API remarks state a key "is not kept alive only because it is a member of the collection … even if it can be reached directly from a value stored in the table" ([CWT remarks]). So `value == key`, value→key closures, and value→delegate→`DynamicMethod`→ALC→key chains all collapse together once nothing *outside* the table roots the key. The ticket's premise that a value referencing its key "defeats collection" is **not** what the runtime does — the documented edge is the opposite direction: (a) the value lives exactly as long as the key, so a value that keeps the *table* alive leaks the table ([dotnet/runtime#12255], [corefx#19765], [docs#3273]); and (b) any *other* rooted path into the value (e.g. the same accessor stored in a second, strong collection, or an event handler) pins through it as usual.

C6 is the case most relevant to FastReflection: a delegate whose `DynamicMethod` was created with `owner = <collectible declaring type>` (the `EmitHelper.CreateDynamicMethod` shape) *would* pin the ALC if strongly rooted — but as a `CWT` value it dies with the key, so a `CWT<Type, accessor>` holding such delegates unloads cleanly. C11 confirms the doc-listed pin ("delegate to a static method of the assembly") only applies while rooted; under the ephemeron it is not.

### 4. Empirical results (compiled for net472 against the installed 4.7.2 targeting pack, run on the machine's .NET Framework 4.x, Release)

| # | Scenario | Result |
|---|---|---|
| F0 | `Type` loaded in default domain, **cached** in `Dictionary` — `WeakReference` after GC | **ALIVE** |
| F1 | `Type` loaded in default domain, **not cached** — `WeakReference` after GC | **ALIVE** |
| F2a-c | child `AppDomain` loads Plugin; `Type` sent via `AppDomain.SetData`/`GetData` to default domain | `GetData` returns `System.RuntimeType` **of the default domain** — `Type.Name` resolves; earlier run threw `FileNotFoundException` until `Plugin.dll` was made resolvable in the default domain, i.e. marshaling the `Type` **loaded the plugin into the receiving domain** |
| F2 | `AppDomain.Unload` while the default-domain static cache holds the transferred `Type` | succeeded; using the cached `Type` afterwards works fine — it is the default domain's own `RuntimeType`, not a reference to the child |
| F4 | `RunAndCollect` `TypeBuilder` type, `WeakReference` | dead — collectible dynamic assemblies do unload on .NET Framework |
| F5 | `CWT<Type,object>` on a `RunAndCollect` type | dead |
| F6 | ordinary dynamic type inheriting a collectible type | `NotSupportedException` |

So on net462 the static-cache question largely dissolves: ordinary assemblies unload only via `AppDomain.Unload`, which is forced (threads aborted, structures freed — [how-to-unload], [unloadability §1]) rather than reachability-based, and cannot target the default domain. A `RuntimeType` in a non-unloaded domain is rooted by the runtime itself — F1 shows `WeakReference` to an *uncached* loaded `Type` stays alive, so no cache discipline changes the outcome. The genuinely dangerous interaction is the F2 marshaling: `RuntimeType` travels by value (assembly-qualified identity), so merely *receiving* a plugin's `Type` in the host domain loads that assembly into the host domain, where it can never be unloaded. (An `AppDomain` "proxy" story doesn't apply to `Type`/`MemberInfo` — they don't arrive as remoting proxies; `CannotUnloadAppDomainException` remains possible whenever a thread can't unwind — [how-to-unload].)

### 5. Patterns × TFM

| Pattern | net462 | netstandard2.0 asset on .NET Core | net8.0 |
|---|---|---|---|
| `ConcurrentDictionary`/static field holding `Type`/`MemberInfo`/delegates | Moot — assembly already pinned for domain life (F0/F1); still leaks `RunAndCollect` types (R1 analogue) | Pins any collectible ALC whose types are cached (C2/C14); the library's statics live in whichever ALC loaded the library | Same: pins (C1/C2/C14); documented pin cause ("a static variable…") [unloadability] |
| `ConditionalWeakTable<Type/MemberInfo, V>` | Releases `RunAndCollect` keys (F5); pointless for normal types — rooted regardless (F1) | Releases collectible keys incl. `value==key`, closures, `DynamicMethod`-owned delegates, `MemberInfo` keys (C3-C12) | Same. Costs: `TValue` must be a class; reference-equality keys (fine for `RuntimeType`); API gaps — `GetOrAdd` is .NET 10+ only, `AddOrUpdate` and enumeration are .NET Core/netstandard2.1+, so the shared surface on all three TFMs is `Add`/`TryGetValue`/`GetValue`/`GetOrCreateValue`/`Remove` ([CWT-GetOrAdd], [CWT-AddOrUpdate]); value dies with the key (one GC fixpoint later); a value that references *the table itself* leaks the table ([#12255]) |
| `WeakReference`/`WeakReference<T>` values or `Dictionary<name, WeakReference>` | Same caveat — immortal types anyway | Releases (C13); costs: stale-entry cleanup, re-resolution after unload (can't re-resolve an unloaded type — a miss must re-enter a live context), `WeakReference` is the documented unload-safe handle | Same |
| Per-context cache (static inside the collectible ALC / plugin-owned) | Natural fit for AppDomain model — per-domain statics die with the domain | Dies with the ALC (C9); cost: the host-side library cannot own it — the plugin or a per-ALC indirection must; `AssemblyLoadContext` type is **not in the netstandard2.0 surface**, so the asset can't even test `IsCollectible` or key by context without a facade | Same; `AssemblyLoadContext`/`MemberInfo.IsCollectible` available |
| `DynamicMethod` hosted by a collectible owner (`EmitHelper` shape) | Method collectible with owner; on Framework collectible assemblies apply to emitted types only — hosted DMs over normal types just work; over `RunAndCollect` types they unload together | Delegate→DM→owner's loader allocator pins ALC **while rooted**; under CWT the whole cycle collects (C6). Cost: `skipVisibility` demands satisfied via owner hosting | Same. Anonymous `DynamicMethod` has its own collectible allocator and works on Core (C10); but an anonymous DM's IL referencing collectible members still reaches the ALC through the delegate — rooted ⇒ pin, CWT ⇒ release |
| `DynamicMethod` anonymously hosted | Same availability (net462 supports unowned DMs) | "Isolated… in a system-provided assembly", restricted skip-visibility ([DynamicMethod remarks]) — insufficient for the non-public member access `CreateDynamicMethod` needs (`skipVisibility: true` + owner) | Same |
| Delegates/`MethodInfo.Invoke` closures as cached values | Pin nothing that isn't already pinned | Rooted ⇒ pin (fallback lambdas close over collectible `MethodInfo`, C7 shape); CWT ⇒ release | Same |
| `TypeBuilder` + `AssemblyBuilderAccess.RunAndCollect` | Works (F4/F5); restrictions: transient-only, no COM/PInvoke/`calli`/marshaling/thread-statics; ordinary dynamic types can't statically reference collectible types — `NotSupportedException` (F6) | Needs full `System.Reflection.Emit` (TypeBuilder isn't in the `ILGeneration`/`Lightweight` packages the repo references on netstandard2.0); same `NotSupportedException` for mixed assemblies | Works (R0); same restrictions; unload verified |
| AppDomain isolation | `AppDomain.Unload` is forced; `Type`/`MemberInfo` marshal **by value** — cross-domain transfer loads the assembly into the receiver (F2); default domain never unloads; `CannotUnloadAppDomainException` possible ([how-to-unload]) | n/a | n/a (single domain) |

### 6. What this does **not** establish

- Whether `CWT` entry removal is timely under sustained GC pressure (the probe uses a tight GC loop; the docs' own loop acknowledges "more passes might be needed" with finalizers).
- Anything about `MemberInfo.IsCollectible`/`AssemblyLoadContext` introspection performance, or about `RuntimeType` identity semantics (CWT keys are `ReferenceEquals`; each collectible ALC mints its own `RuntimeType` for the same `Plugin.Target`, so after unload and re-load a CWT keyed on the old `Type` misses — observed implicitly: each case loaded a fresh ALC and fresh `RuntimeType`).
- Whether a collectible ALC in which *FastReflection itself* is loaded behaves differently (probe keeps the library in the default context — the common plugin-host layout).

## Caveats and unknowns

- Probe environment: one machine, .NET SDK 8.0.426 running `net8.0` (Microsoft.NETCore.App 8.0.x), .NET Framework 4.7.2 targeting pack, Release builds, `MethodImpl(NoInlining)` on every load/case method, 10× `GC.Collect()`+`WaitForPendingFinalizers()` per case. `ALIVE` = survived all 10 cycles; `dead` = `WeakReference.IsAlive == false`. No WinDbg/SOS `!dumpalc`/`gcroot` verification of the exact root chain for C1/C2/C14 — the static dictionary is the presumed root by construction.
- The net472 probe does not exercise `LoaderAllocator` reclamation directly (Framework lacks `LoaderAllocator` visibility); F4/F5 rely on `WeakReference` death of the created `Type` as the collectible-assembly signal per the collectible-assemblies doc.
- `ConditionalWeakTable` convenience-API availability (`GetOrAdd` net10+, `AddOrUpdate`/enumeration .NET Core+) is taken from the API reference moniker lists; the net472 probe itself used only `Add`.
- The probe does not test `netstandard2.0`-compiled caching code running on .NET Framework — that leg is behaviorally identical to net462 for GC purposes (same CLR) but was not run.
- The "value references key defeats collection" premise in the ticket is contradicted by docs and by C4/C5/C12; the only self-reference leak found in primary sources is value→*table* ([dotnet/runtime#12255]). Whether some other runtime (Mono, Unity's IL2CPP/Burst, NativeAOT) implements ephemerons with this edge case was not tested — and `EmitHelper.DynamicCodeSupported` already excludes NativeAOT/full AOT paths.
- `ConditionalWeakTable` adds a GC-time cost (ephemeron marking is a fixpoint); not measured.
- C11 used a `static` plugin method for `CreateDelegate`; instance-bound delegates were not tested (a bound-to-instance delegate would pin an *instance*, an orthogonal pin).
- Probe code is throwaway under `%TEMP%\fr-probe-alc` (paths in the appendix); not part of the repo.

## Implications for the decision ticket

- Seat M/Q's claim stands: `s_cache` ([TypeAccessor.cs L32]) plus the accessor's `Type`/`MemberInfo`/delegate contents pin any collectible type passed to `TypeAccessor.Create`, on every TFM where collectible unloading exists (probe C1).
- On net8.0 (and the netstandard2.0 asset under .NET Core), a `ConditionalWeakTable<Type, …>`-shaped cache survives contact with every realistic FastReflection value — including `RuntimeTypeAccessor` itself (Type+MemberInfo+emitted delegates) and delegates hosted in the collectible ALC (C6, C12). The ephemeron's documented key-non-persistence holds even when values reach back to keys.
- A `CWT` on net462 only pays off for `RunAndCollect`/`DynamicMethod`-created types (F4/F5); for assembly-loaded types the runtime already roots them for the domain's life (F1), and AppDomain boundaries neither allow holding foreign `Type`s nor provide cooperative unload.
- `RunAndCollect` exists on all three targets' runtimes (net472 verified) but carries a non-trivial restriction set (no ordinary-assembly references into it, no save, Framework-only COM/PInvoke/marshal bans) and requires full `System.Reflection.Emit`, absent from the netstandard2.0 package set the library references today.
- `DynamicMethod` hosting choice (`EmitHelper.CreateDynamicMethod` passes the declaring type as `owner` for `skipVisibility`) is *not* itself the pin — the static dictionary is; the same delegates release under a weak-keyed cache (C6). Anonymous hosting would change visibility semantics (restricted skip-visibility only) more than collectibility.

## Sources

Primary docs (Microsoft Learn; dotnet/docs gitcommit links where the page exposes them; all read 2026-10-24):

- [UNLOAD]: [How to use and debug assembly unloadability in .NET](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability) — [source at 156931bb](https://github.com/dotnet/docs/blob/156931bb4ec1e81b028c76ea983553f2e9778bdd/docs/standard/assembly/unloadability.md): cooperative unload conditions, strong-reference pin list ("a stack slot or a processor register … a static variable, or a strong (pinning) GC handle"), `WeakReference` carve-out, GC-loop completion check, `!dumpheap -type LoaderAllocator`/`!gcroot` debugging, `Unloading` event, ReadyToRun ignored in collectible ALCs.
- [COLLECT]: [Collectible assemblies for dynamic type generation](https://learn.microsoft.com/en-us/dotnet/fundamentals/reflection/collectible-assemblies) — [source at 20dfcbac](https://github.com/dotnet/docs/blob/20dfcbac7f55f648d6c44e4add846557e2f81501/docs/fundamentals/reflection/collectible-assemblies.md): lifetime list ("a delegate to a static method that's defined in a module of the assembly", `RuntimeTypeHandle`, `TypeBuilder`/`ModuleBuilder`/`ILGenerator`/`LocalBuilder` liveness) and restrictions (`NotSupportedException` for ordinary→collectible static references; netfx COM/PInvoke/marshal/thread-static bans).
- [CWT-API]: [ConditionalWeakTable&lt;TKey,TValue&gt;](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2): remarks — "It does not persist keys … even if it can be reached directly from a value stored in the table … automatically removes the key/value entry as soon as no other references to a key exist outside the table"; source links to [ConditionalWeakTable.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/ConditionalWeakTable.cs) (ephemeron/`DependentHandle` implementation).
- [DM-API]: [DynamicMethod](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.dynamicmethod): remarks — "The executable code created by the JIT compiler is reclaimed when the DynamicMethod object is reclaimed"; anonymously hosted vs module/type-associated hosting; skip-visibility permission tables.
- [APPUNLOAD]: [How to: Unload an Application Domain](https://learn.microsoft.com/en-us/dotnet/framework/app-domains/how-to-unload-an-application-domain) — [source at 156931bb](https://github.com/dotnet/docs/blob/156931bb4ec1e81b028c76ea983553f2e9778bdd/docs/framework/app-domains/how-to-unload-an-application-domain.md): forced unload, "all application domain–specific data structures are freed", domain-neutral assemblies never unload, `CannotUnloadAppDomainException`.
- [ALC-UNDERSTAND]: [Understanding AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext): collectible contexts and the shared/default context model.
- [DEPhANDLE]: [DependentHandle](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.dependenthandle): "will not cause the target to be kept alive if there are no other strong references to it, but it will do so for the dependent object instance as long as the target is alive" (ephemeron semantics backing CWT).
- [ISCOLLECT]: [MemberInfo.IsCollectible](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.memberinfo.iscollectible) / [AssemblyLoadContext.IsCollectible](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext.iscollectible) / [AssemblyBuilderAccess](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.assemblybuilderaccess): detection APIs and the `RunAndCollect` enum member (monikers: net472 included).
- [CWT-GetOrAdd]: [ConditionalWeakTable.GetOrAdd](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2.getoradd) — monikers net-10.0/net-11.0 only.
- [CWT-AddOrUpdate]: [ConditionalWeakTable.AddOrUpdate](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2.addorupdate) — monikers netcore-2.0+, netstandard-2.1, net-5+; absent from netfx/netstandard2.0.

Runtime issues / discussions:

- [dotnet/runtime#12255](https://github.com/dotnet/runtime/issues/12255) (= coreclr#23223, corefx#19765): CWT leak when a **value references the table itself** while a key stays rooted — the documented self-reference edge; distinct from value→key, which collects.
- [dotnet/docs#3273](https://github.com/dotnet/docs/issues/3273): doc tracking of the same caveat.

Repo files (worktree `research/cache-alc-unload`, read 2026-10-24): [TypeAccessor.cs L16-32, L44-46, L83-138, L199-227](src/FastReflection/TypeAccessor.cs); [Member.cs L8-13](src/FastReflection/Member.cs); [EmitHelper.cs L43-47](src/FastReflection/Emit/EmitHelper.cs); [AccessorEmitter.cs L33-44, L83-95, L133-151, L216-235, L333-344](src/FastReflection/Emit/AccessorEmitter.cs).

## Appendix: probe

Location `C:\Users\98217\AppData\Local\Temp\fr-probe-alc` (throwaway): `Plugin` (netstandard2.0: `Plugin.Target` with property `Value`, field `Name`, `GetValue()`, static `StaticHello()`, and `Plugin.SelfCache` static holder); `Probe` (net8.0, references the worktree `FastReflection.dll` net8.0 Release build); `Probe472` (net472, resolved against the installed v4.7.2 targeting pack).

Key structure — one collectible ALC per case, host-side static caches, `NoInlining` load methods, `WeakReference`s to ALC/Assembly/Type, 10× GC loop:

```csharp
private sealed class CollectibleAlc : AssemblyLoadContext
{
    internal CollectibleAlc() : base(isCollectible: true) { }
    protected override Assembly Load(AssemblyName name) => null;
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static WeakReference[] CaseTypeAccessor(string plugin)
{
    var alc = new CollectibleAlc();
    var asm = alc.LoadFromAssemblyPath(plugin);
    var t = asm.GetType("Plugin.Target");
    var w = new[] { new WeakReference(alc), new WeakReference(asm), new WeakReference(t) };
    _ = TypeAccessor.Create(t);            // s_cache in default context pins it
    alc.Unload();
    return w;
}
```

Observed output (net8.0, Release):

```
C0 control (no cache)                                          alc:dead  asm:dead  type:dead
C1 FastReflection TypeAccessor.Create(t)                       alc:ALIVE asm:ALIVE type:ALIVE
C2 ConcurrentDictionary<Type,object>, value=string             alc:ALIVE asm:ALIVE type:ALIVE
C3..C13 all CWT/per-context/weak variants                      alc:dead  asm:dead  type:dead
C14 ConcurrentDictionary<string,object>, value holds Type      alc:ALIVE asm:ALIVE type:ALIVE
R0 RunAndCollect TypeBuilder, no cache                         dead/dead/dead
R1 RunAndCollect type in ConcurrentDictionary                  type:ALIVE
R2 ordinary dynamic type inheriting collectible type           NotSupportedException
```

Observed output (net472, Release): `F0/F1 typeRef:ALIVE` (loaded `Type` immortal whether cached or not); `F2 GetData → System.RuntimeType` (marshaled by value — first run `FileNotFoundException` until `Plugin.dll` resolvable in the default domain; use-after-unload succeeds because it's the local copy); `F4/F5 typeRef:dead` (RunAndCollect unloads; CWT releases); `F6 NotSupportedException`.

# Which public APIs of fast-member, Dynamitey, Namotion.Reflection and DotNext do real dependents call

Research note for GitHub issue Skymly/FastReflection#2 (feeds every scope ticket). Date: 2026-10-24. Evidence only; no decision is made or recommended here.

## Question

Which public APIs of fast-member, Dynamitey, Namotion.Reflection and DotNext (including the deprecated DotNext.Reflection) do real dependents actually call? The answer calibrates the "daily use first" pruning rule and feeds every scope ticket.

## Short answer

The used surface of each library is a small, mostly non-overlapping core.

- **fast-member** (16 repos sampled): `TypeAccessor.Create` + the `accessor[target, "name"]` indexer is the dominant pattern (10 repos, ~24 `Create` sites). `GetMembers()` + `Member.Name`/`Type`/`Ordinal` for schema/column enumeration is second (~7 repos). `ObjectReader.Create` → `SqlBulkCopy` is a self-contained second scenario (4 repos). `ObjectAccessor.Create` is rare (1 repo), `CreateNew()` and `[Ordinal]` seen once each, and `CreateNewSupported`/`GetMembersSupported`/`MemberSet`-as-list were never called.
- **Dynamitey** (13 repos): production code almost only calls `Dynamic.InvokeGet`/`InvokeSet`/`InvokeMember`/`InvokeMemberAction`, `Dynamic.GetMemberNames`, and `Dynamic.InvokeConvert` (8 repos). `InvokeContext`, `CacheableInvocation.CreateCall` and `InvokeConstructor` appear once each (renode, RCaron). `Curry`, `CoerceToDelegate`, operator/event/index/chain variants are confined to the author's own test suite and a demo project. `DynamicObjects.*`, `Tupler`, `Builder`, `FastDynamicInvoke`, `ConvertAll`/`ConvertEach`, `InvokeCallSite`, `ThisFunc`/`ThisAction` delegates: zero external call sites.
- **Namotion.Reflection** (9 repos): the RicoSuter ecosystem (NJsonSchema, NSwag) uses nearly the whole surface — string-based type checks (`FirstAssignableToTypeNameOrDefault`, `IsAssignableToTypeName`, `GetAssignableToTypeName`), the contextual nullability tree (`ToContextualType`, `GetContextualProperties`/`Accessors`, `.Nullability`/`.GenericArguments`/`.OriginalType`), XML docs (`GetXmlDocsSummary`/`Remarks`/`Tag`/`Element`), context attributes and `HasProperty`/`TryGetPropertyValue`. Outside that ecosystem the usage collapses to `GetXmlDocsSummary`/`GetXmlDocsRemarks` (saunter, SlimMessageBus, commanddotnet-tests) and a single `ToContextualAccessor` (TypeGen). The runtime nullability *validation* trio (`HasValidNullability`/`EnsureValidNullability`/`ValidateNullability`) has zero call sites anywhere sampled.
- **DotNext**: the still-shipped `DotNext.Reflection` namespace produced only four distinct APIs in five call sites across 14 repos — `TypeExtensions.GetBaseTypes()` (KurrentDB, and a fork), `EnumType.GetCustomAttribute<TEnum,TAttribute>()` (KurrentDB), `MethodExtensions.GetParameterTypes()` (two repos). The deprecated `DotNext.Reflection` package (137.9K lifetime downloads, zero registered dependents on nuget.org) was found referenced in 4 repos; its only real call sites are one benchmark project using `Type<T>.Property<TValue>.RequireGetter`/`RequireSetter`/`MemberGetter`/`MemberSetter`/`DynamicInvoker`. `Type<T>.Method`, `Reflector.Unreflect`, `ExtensionRegistry`, `Function`/`Procedure`/`Ref<T>`: no external call sites found.

A consistent cross-cutting finding: a sizeable minority of declared package references produce **zero** call sites — test-only references (Dapper, EFCore.BulkExtensions, XamlCSS on FastMember; commanddotnet on Namotion), dead references left after code moved (NBi on Dynamitey; sisk, neo, VisualJsonEditor on Namotion; datatent2, Raylib-ECS-Prototype, BeyondDimension/Common on DotNext.Reflection; SuperMemoAssistant on FastMember), and pure-transitive listings (VPet, ShokoDesktop). 14 of the 52 sampled repos show no shipping-code usage of the library they were listed as depending on.

## Findings

### 1. fast-member (package `FastMember` 1.5.0)

nuget.org lists 241 dependent NuGet packages and 26 GitHub repositories; 67.1M total downloads, 1.5.0 is 49.9M of that ([FM-NuGet]). The public surface is small: `TypeAccessor`, `ObjectAccessor`, `ObjectReader`, `MemberSet`/`Member`, `OrdinalAttribute` ([FM src]).

**Sampled dependents** (16 repos cloned at HEAD; stars from GitHub API 2026-10-24): DapperLib/Dapper (18.4K), borisdj/EFCore.BulkExtensions (4.0K), serenity-is/Serenity (2.7K), LorisYounger/VPet (7.0K), paillave/Etl.Net (813), AudioBand/AudioBand (559), warappa/XamlCSS (337), spaghettidba/WorkloadTools (264), papyrus-mc/papyruscs (264), supermemo/SuperMemoAssistant (214), WolvenKit/WolvenKit-7 (106), rebus-org/Tababular (96), rebus-org/Debaser (53), mookid8000/Topos (25), perfectsquircle/toadstool (10), misha130/Dapper.Builder (9). The first 10 come from nuget.org's GitHub-dependents list; the rebus/mookid8000/perfectsquircle/misha130 repos cover the top NuGet-package dependents (Tababular 855.3K dl, Topos 510.1K dl) plus code-search hits.

**Usage table** (call sites counted with ripgrep over `.cs`; "repos" = sampled repos with ≥1 shipping-code call):

| API | Repos | Sites | Representative call sites |
|---|---|---|---|
| `TypeAccessor.Create(type)` | 9 + 1 test | ~24 | [Serenity L161], [Etl.Net L18], [Tababular L35], [Debaser L379], [Topos L77], [toadstool L18], [Dapper.Builder L74], [WolvenKit CVariable L28], [AudioBand ObservableObject L22] (`Create(type, allowNonPublicAccessors: true)`), [XamlCSS tests L92] |
| indexer `accessor[target, "name"]` get / set | 5 | ~30 | get: [Serenity L166], [Etl.Net L25]; get+set: [WolvenKit CVariable L248-588], [Debaser Activator L43], [AudioBand setting L50,L69], [XamlCSS test L187] |
| `GetMembers()` → `MemberSet` | 7 | ~37 | [Tababular L75], [Debaser L380], [Topos L78], [toadstool L46], [Dapper.Builder L37,75,81], [WolvenKit REDReflection L205-217], [AudioBand ObservableObject L125, AudioSourceExtensions L23] |
| `Member.Name` / `.Type` / `.Ordinal` / `.IsDefined` / `.GetAttribute` / `.CanRead` / `.CanWrite` | 7 | — | `member.Name` is by far the most-read member property (WolvenKit, Tababular, Debaser, Topos, toadstool, Dapper.Builder, AudioBand); `Ordinal` used for ordering in [WolvenKit REDReflection L206-217]; `IsDefined(typeof(...))` in [AudioBand ObservableObject L125]; `GetAttribute` in [Dapper.Builder L82]; `CanWrite`/`CanRead` only in [toadstool L47, ReflectionHelper L14] |
| `ObjectReader.Create<T>(source, members)` | 2 + 2 test | 7 | [WorkloadTools L782, L914], [papyruscs L16, L43, L80]; tests: [Dapper test L57], [EFCore.BulkExtensions test L1552]. All feed `SqlBulkCopy`/`IDataReader` |
| `ObjectAccessor.Create(obj)` | 1 | 2 | [AudioBand ObservableObject L90], [AudioBand AudioSourceExtensions L21]; indexer `accessor["name"]` get/set [AudioBand setting L50, L69] |
| `OrdinalAttribute` | 1 | ~23.6K decls | `[Ordinal(1000)]` on generated buffered types, [WolvenKit BufferedClasses L21-36]; `Member.Ordinal` orders reads |
| `TypeAccessor.CreateNew()` | 1 | 1 | [toadstool L25] |
| `CreateNewSupported` / `GetMembersSupported` / `ObjectReader` ctor / `MemberSet` indexer / `Member.MemberInfo` | 0 | 0 | not found in sample |

Additional detail: WolvenKit-7 is a heavy consumer — `using FastMember` in ~6,826 `.cs` files (mostly generated `CVariable` subclasses), with all reads/writes funnelled through `CVariable.cs`'s `TypeAccessor` field ([WolvenKit CVariable L28-39]); it also references `DotNetHelper.FastMember.Extension` 2.0.0 ([WolvenKit csproj L39]), a community extension package on FastMember. Etl.Net implements its own `DbDataReader` (`ObjectDataReader`) on top of `TypeAccessor` rather than using `ObjectReader` ([Etl.Net L18-25]).

Non-usage evidence: Dapper references FastMember **only in tests** ([Dapper props], [Dapper.Tests csproj]); EFCore.BulkExtensions likewise only in test projects at HEAD ([EFBE test csproj]); SuperMemoAssistant has three `FastMember` PackageReferences but zero call sites ([SMA csproj]); VPet has no reference at all — its nuget.org GitHub-dependents listing is presumably transitive (no `FastMember` string anywhere in the repo).

### 2. Dynamitey (package `Dynamitey` 3.0.3; still-heavy 2.0.9.136)

nuget.org lists 100 dependent packages and 11 GitHub repositories; 35.0M total downloads, of which 2.0.9.136 is 27.5M ([DY-NuGet]). The largest dependent is the same author's ImpromptuInterface (35.1M) followed by SpecFlow.Assist.Dynamic (7.7M) and Reqnroll.Assist.Dynamic (1.1M) ([DY-NuGet pkgs]). The public surface is wide: ~37 static entry points on `Dynamitey.Dynamic` plus context wrappers (`InvokeContext`, `StaticContext`, `InvokeArg`, `InvokeMemberName`), `CacheableInvocation`, the `DynamicObjects.*` prefab objects, `Builder`, `Tupler`, `Curry`, `ThisFunc`/`ThisAction` delegates ([DY Dynamic.cs], [DY tree]).

**Sampled dependents** (13 repos): ekonbenefits/impromptu-interface (679★), reqnroll/Reqnroll (795), marcusoftnet/SpecFlow.Assist.Dynamic (52), Seddryck/NBi (106), Coldairarrow/EFCore.Sharding (705), splitice/IPTables.Net (44), renode/renode-infrastructure (123), ShokoAnime/ShokoDesktop (104), Azure/azure-iot-remote-monitoring (248, archived 2018), Jan0660/RCaron (0), UlyssesWu/Agency (5), aslotte/MLOps.NET (81), CoFlows/CoFlows-CE (23).

**Aggregate `Dynamic.*` counts across the sample** (shipping + test code together):

| Rank | API | Sites | Who calls it |
|---|---|---|---|
| 1 | `InvokeMember` | 30 | impromptu tests 22, RCaron 2, Agency 2, CoFlows 2, EFCore.Sharding 1, renode 1 |
| 2 | `InvokeGet` | 20 | impromptu tests 5, Reqnroll 2, SpecFlow.Assist.Dynamic 2, EFCore.Sharding 2, IPTables.Net 2, renode 2, azure-iot 2, CoFlows 1, MLOps.NET 1, Agency 1 |
| 3 | `InvokeConstructor` | 11 | impromptu tests 9, renode 1, Agency 1 |
| 4 | `InvokeConvert` | 10 | renode 6 ([renode MonitorCommands L743, L813; TypeExtensions L119-154]), impromptu tests 2, RCaron 1, Agency 1 |
| 5 | `GetMemberNames` | 9 | Reqnroll 2, SpecFlow.Assist.Dynamic 2, IPTables.Net 1, azure-iot 2, impromptu tests 2 ([Reqnroll L101, L158]; [IPTables L42]; [azure-iot L152, L470]) |
| 6 | `InvokeSet` | 8 | impromptu tests 4, EFCore.Sharding 1, renode 1, CoFlows 1, Agency 1 |
| 7 | `InvokeMemberAction` | 7 | RCaron 2, Agency 2, EFCore.Sharding 1, renode 1, impromptu tests 1 |
| 8 | `InvokeGetIndex` / `InvokeSetIndex` | 5+3 | renode ([renode DeviceHelper L26, L39]), impromptu tests, Agency |
| 9 | `InvokeSetChain` / `InvokeGetChain` | 4+3 | impromptu tests, Agency |
| 10 | operator & event: `InvokeBinaryOperator` 4, `InvokeAddAssignMember` 5, `InvokeSubtractAssignMember` 6, `InvokeIsEvent` 3 | 18 | impromptu tests + Agency only |
| 11 | `Linq` 5, `Invoke` 2, `InvokeAction` 2, `InvokeSetAll` 3, `Curry` 2, `CoerceConvert` 2, `CoerceToDelegate` 1, `CreateCallSite` 1 | 22 | `Curry`+`CoerceToDelegate` only in [Agency Agent.cs L73-79] (`Dynamic.CoerceToDelegate(Dynamic.Curry(c, pCount)(...))`); `Linq`/`CreateCallSite`/`InvokeSetAll`/`CoerceConvert` in impromptu tests; `Dynamic.Invoke` in [Agency RemoteInvocation L184, L192] |
| 0 | `FastDynamicInvoke`, `InvokeCallSite`, `InvokeUnaryOperator`, `ConvertAll`, `ConvertEach`, `IsDBNull`, `ClearCaches`, `ApplyEquivalentType`, `GenericDelegateType`, `InvokeSetValueOnIndexes` | 0 | never called |

**Supporting types**: `InvokeContext` is the one context type with real use — `InvokeContext.CreateStatic`/`CreateContext` in [renode DeviceHelper L87, L149, L161] and [RCaron Motor.cs L766-783], plus inspection of `.StaticContext`/`.Context`/`.Target` in [impromptu Util.cs L74-83]. `CacheableInvocation.CreateCall` + `InvocationKind` appear once, in a language runtime ([RCaron binder L134-137]). Of the whole `DynamicObjects` namespace only `AggreType`/`PropretySpecType` are used — inside [impromptu ActLikeProxy.cs L99-110]; MLOps.NET's `using Dynamitey.DynamicObjects` is vestigial ([MLOps L1]). `InvokeArg`, `InvokeMemberName`, `Tupler`, `Builder`/`Build<>`, `Dictionary`/`List`/`Mimic`/`Recorder`/`LateType`/`Lazy`/`FluentStringLookup`/`RegexMatch`/`Expando`, `FluentRegex`, `ThisFunc`/`ThisAction`/`Return<TR>`: no external call sites.

Notes on the sample's shape: SpecFlow.Assist.Dynamic (Dynamitey 2.0.9.136) and Reqnroll.Assist.Dynamic (3.0.3) have an identical, minimal profile — `GetMemberNames` + `InvokeGet` in one file each ([SpecFlow L106-184], [Reqnroll L101-179]). ImpromptuInterface's *shipping* code touches only `InvokeContext` + two `DynamicObjects` types; its broad `Dynamic.*` coverage lives in its own test suite. NBi keeps `Dynamitey` references in csproj+packages.config with zero call sites ([NBi csproj]); ShokoDesktop's `using Dynamitey;` is dead — it only calls `Impromptu.ActLike<T>` ([Shoko ClientFactory L155]), i.e. reaches Dynamitey transitively through ImpromptuInterface.

### 3. Namotion.Reflection (3.5.1; dependents on 2.1.1–3.5.1)

nuget.org lists 76 dependent packages and 18 GitHub repositories; 333.1M total downloads ([NR-NuGet]). The top dependents are the author's own ecosystem — NJsonSchema (412.1M), NSwag.Core (222.1M), NSwag.Generation (148.3M), NSwag.AspNetCore (144.6M) ([NR-NuGet pkgs]) — so the download figure is almost entirely NJsonSchema/NSwag throughput (NJsonSchema alone exceeds the library's total).

**Sampled dependents** (9 repos): RicoSuter/NJsonSchema (1.6K★, on 3.5.0), RicoSuter/NSwag (7.4K, 3.5.0), jburzynski/TypeGen (235, on 2.1.1), asyncapi/saunter (224, on 3.1.1), bilal-fazlani/commanddotnet (612), zarusz/SlimMessageBus (660, on 3.5.1), sisk-http/core (210, on 3.3.0), neo-project/neo (3.5K, test ref), RicoSuter/VisualJsonEditor (373, packages.config).

**Usage table** (aggregate call sites across the whole sample):

| Rank | API | Sites | Who |
|---|---|---|---|
| 1 | `FirstAssignableToTypeNameOrDefault<T>` | 67 | NJsonSchema 45, NSwag 22 |
| 2 | `IsAssignableToTypeName` | 34 | NJsonSchema 11, NSwag 23 |
| 3 | `ToContextualType` | 28 | NJsonSchema 17, NSwag 11 ([NJS JsonSchemaGenerator L76-976]) |
| 4 | `HasProperty` / `TryGetPropertyValue<T>` | 19+18 | NJsonSchema 6+7, NSwag 13+11 |
| 5 | `GetAssignableToTypeName<T>` | 18 | NJsonSchema 4, NSwag 14 |
| 6 | XML docs: `GetXmlDocsSummary` 13, `GetXmlDocsRemarks` 10, `GetXmlDocsTag` 2, `GetXmlDocsElement` 1, `GetXmlDocs(param)` 1 | 27 | NJsonSchema ([NJS TypeExtensions L75-123], [JsonSchemaGenerator L473,493,884]), NSwag ([NSwag OperationResponseProcessorBase L106]), saunter ([saunter L222-283]), SlimMessageBus ([SMB L164-229]), commanddotnet tests ([CD ResourceGenerators L112]) |
| 7 | `ToCachedType` | 8 | NJsonSchema 6, NSwag 2 |
| 8 | context attributes: `GetContextOrTypeAttributes` 8, `GetContextAttributes` 8, `GetContextOrTypeAttribute<T>` 3, `GetContextAttribute<T>` 2, `IsContextAttributeDefined<T>` 2 | 23 | NJsonSchema, NSwag |
| 9 | `ToContextualParameter` 5, `ToContextualProperty` 2, `ToContextualAccessor` 2 | 9 | NSwag 7, TypeGen 1 ([TypeGen L108-109] + `.Nullability`) |
| 10 | `GetContextualProperties` 5, `GetContextualAccessors` 2 | 7 | NJsonSchema ([NJS JsonSchemaGenerator L798], [JsonReferenceResolver L23,326], [JsonSchema.cs L28]), NSwag ([NSwag OperationParameterProcessor L430]) |
| 11 | `InheritsFromTypeName` 5, `GetDisplayName` 5, `GetCommonBaseType` 1 | 11 | `InheritsFromTypeName`/`GetCommonBaseType` NSwag; `GetDisplayName` hits turned out to be commanddotnet's own same-named interface method — actual Namotion `GetDisplayName` calls: **0** |
| — | contextual members: `.Nullability`, `.GenericArguments`, `.OriginalType`, `.Type`, `.Name`, `.AccessorType`, `.EnumerableItemType`, `ContextualAccessorInfo`, `Nullability` enum | pervasive in NJS/NSwag | [NJS JsonSchemaGenerator L607-805], [TypeGen L109] |
| — | `XmlDocs.ClearCache()` + `CachedType.ClearCache()` | 2 | NSwag middleware ([NSwag OpenApiDocumentMiddleware L106-107]); `XmlDocsOptions` built by NJsonSchema ([NJS XmlDocsSettingsExtensions L23-25]) |
| 0 | `HasValidNullability` / `EnsureValidNullability` / `ValidateNullability` / `DisableNullabilityValidation`, `GetEnumerableItemType` ext, `GetXmlDocsPath`, `ToXmlDocsContent`, `GetXmlDocsRecordPropertySummary`, `GetContextualFields`, `IPropertyReader`/`IPropertyWriter`, `PropertyReader`/`PropertyWriter` | 0 | none in sample |

The shape is consistent: NJsonSchema + NSwag exercise essentially the whole public surface (~25 distinct APIs each in shipping code, across 19 and 21 files); every other real consumer sampled uses a two-to-four-method subset — XML-docs readers for schema/doc generators (saunter: summary+remarks; SlimMessageBus Host.AsyncApi: summary+remarks, [SMB L164-229]) or one call (TypeGen's `ToContextualAccessor().Nullability`). Three of nine sampled repos declare the package but call nothing: sisk-http `Sisk.Documenting` (3.3.0 ref, zero `Namotion` string in code), neo `Neo.UnitTests` (3.5.1 ref, zero usage), VisualJsonEditor (packages.config + installer entry only, transitive of its NJsonSchema use).

### 4. DotNext (`DotNext` 6.10.0) and the deprecated `DotNext.Reflection` package (4.9.0)

Two different things share the `DotNext.Reflection` namespace:

- **Still shipped** inside `DotNext` (11.1M total dl; 42 dependent packages; 6 listed GitHub dependents, [DN-NuGet]): `TypeExtensions` (`GetBaseTypes`, `IsGenericInstanceOf`, `GetGenericArguments`, `IsImmutable`, `IsUnmanaged`, `IsVoid`, `Cast`, `IsAssignableFromWithoutBoxing`, `Devirtualize`, `DefaultValue`), `MethodExtensions` (`GetParameterTypes`, `SignatureEquals`, `TryInvoke`), `TaskType`, `CollectionType`, `DictionaryType`, `EnumType`, `DelegateType`, `DisposableType`, `PropertyExtensions.IsExternalInit`, `CustomAttribute.IsDefined<T>` ([DN Reflection dir @HEAD], [DN api page]).
- **Deprecated** `DotNext.Reflection` package, last version 4.9.0 (net6.0), deprecated since 2023-01 ([DNR-NuGet], [DN#142]): `Type<T>` static lookup (`Type<T>.Method/Property/Field/Constructor/Event/Indexer/Operator/Attribute` with `Get`/`Require`/`GetStatic`/`RequireStatic`/`TryInvoke`/`NewInstance`), `Reflector.Unreflect<D>`/`Unreflect()` → `DynamicInvoker`, `IMember<M,D>`/`Method<D>`/`Constructor<D>`/`Property`/`Field`/`Event`/`Indexer`, `Function`/`Procedure`/`Ref<T>`/`MemberGetter`/`MemberSetter`, `ExtensionRegistry`, `MemberCache` ([DNR tree @refl-4.8]).

**DotNext core sample** (14 repos): LykosAI/StabilityMatrix (9.0K★), kurrent-io/KurrentDB (5.9K), microsoft/durabletask-dotnet (196), JasonBock/Rocks (395), aykutalparslan/Telegram-Server (176), jdermody/brightwire (128), BeyondDimension/Common (25), WorkingRobot/Craftimizer (47), NicciX/ChatAlertsX (0), maxkagamine/Serifu.org (3), TrogonStack/TrogonEventStore (2), sebfischer83/datatent2 (1), calanchenlins/LightBlog (0), jasons-archived/Raylib-ECS-Prototype (2).

**Call sites found in `DotNext.Reflection` namespace (core package)** — four distinct APIs total:

| API | Repos | Sites |
|---|---|---|
| `TypeExtensions.GetBaseTypes()` | 2 | [KurrentDB InMemoryBus.MessageHierarchy.cs L91] and the forked copy in [TrogonEventStore L111] |
| `EnumType.GetCustomAttribute<TEnum, TAttribute>()` | 1 | [KurrentDB ErrorExtensions.cs L58] |
| `MethodExtensions.GetParameterTypes()` | 2 | [Craftimizer Ipc.cs L46] (DotNext 6.1.0), [Serifu.org JsonArrayToRecordConverter.cs L34] (transitive ref — see §5) |
| everything else in the namespace | 0 | — |

For contrast, the same repos use DotNext's *non-reflection* surface much more — KurrentDB alone: `DotNext.Buffers` ×43, `DotNext.Threading` ×34, `DotNext.IO` ×22, `DotNext.Net.Cluster` ×19 ([KurrentDB]); StabilityMatrix, durabletask-dotnet, Rocks, Telegram-Server, brightwire contain zero `DotNext.Reflection` calls.

**Deprecated `DotNext.Reflection` package**: nuget.org reports **no** dependent packages and **no** GitHub dependents ([DNR-NuGet]); 137.9K lifetime downloads (~77/day). GitHub code search for the package ID found 4 `.csproj` references — datatent2 (4.3.0), LightBlog perf project (4.3.0), Raylib-ECS-Prototype (4.7.2), BeyondDimension/Common (`<Using Include="DotNext.Reflection"/>` item, not a PackageReference, [BD csproj L29]). Actual API calls found in exactly one repo — a BenchmarkDotNet comparison: `Type<IndexerEntity>.Property<string>.RequireGetter/RequireSetter("Prop1")`, `MemberGetter`/`MemberSetter` fields, a `DynamicInvoker` declaration ([LightBlog getter L29-71], [LightBlog setter L29-35]). datatent2's `using DotNext.Reflection` sits on an **empty** class ([datatent2 L6-13]); Raylib-ECS-Prototype and Common show zero calls. No external call sites were found for `Type<T>.Method`/`RequireMethod`, `Reflector.Unreflect`, `ExtensionRegistry`, `Function`/`Procedure`/`Ref<T>`, `MemberCache`, indexers, events or operators (also searched via `gh search code` for `Unreflect`/`DotNext.Reflection` in C#).

### 5. Cross-cutting evidence: declared references vs actual calls

- **Declared ≠ used.** In this sample, 10 of 52 repo-level references produce zero API call sites: VPet (FastMember — transitive listing only, no reference at all), SuperMemoAssistant (FastMember, 3 csproj refs), NBi (Dynamitey), ShokoDesktop (Dynamitey — transitive via `Impromptu.ActLike`, [Shoko L155]), sisk-http `Sisk.Documenting`, neo tests, VisualJsonEditor (Namotion), datatent2 + Raylib-ECS-Prototype + BeyondDimension/Common (DotNext.Reflection). Etl.Net holds a second FastMember reference with no usage ([Etl.Net EFCore csproj L11]).
- **Test-only references** matter for ranking: Dapper and EFCore.BulkExtensions reference FastMember only in test projects at HEAD ([Dapper props], [EFBE test csproj]); commanddotnet references Namotion.Reflection only in tests ([CD test csproj L11]). The GitHub dependency graph counts them as dependents regardless.
- **HEAD is not history.** EFCore.BulkExtensions' shipping code no longer uses FastMember at HEAD although the package is a historically large driver of FastMember downloads; current ImpromptuInterface no longer forwards `Impromptu.Invoke*` to `Dynamitey.Dynamic` — its broad `Dynamic.*` usage survives only in its test suite.
- **Same-author skew.** The dominant dependents of Dynamitey (ImpromptuInterface, 35.1M of ~35M) and Namotion.Reflection (NJsonSchema/NSwag, ~all of 333M) are maintained by the libraries' own authors; "typical" usage profiles are therefore largely "author's own ecosystem" profiles. External consumers use much narrower subsets.

## Method and sample

**Method.** (1) Enumerated each library's public API from source: fast-member at `e7423b7` ([FM src]), Dynamitey at `c44f5c5` ([DY tree]), Namotion.Reflection at `e8a92a8`, DotNext core at `3499aed` and the deprecated package at tag `refl-4.8` (the last tagged full surface; the 4.9.0 package on nuget.org has no corresponding repo tag). (2) Picked dependents from nuget.org package pages' "Used By" sections (NuGet-package dependents + GitHub-repository dependents, [FM-NuGet], [DY-NuGet], [NR-NuGet], [DN-NuGet], [DNR-NuGet]) and from GitHub code search (`gh search code '"<PkgId>"' --extension csproj`) for packages nuget.org under-reports (DotNext.Reflection, extra FastMember/Dynamitey repos). (3) `git clone --depth 1` at HEAD into `%TEMP%`; verified each repo's `PackageReference`/`packages.config`/`props` so only projects actually referencing the library count. (4) Counted call sites with ripgrep name patterns per public API; manually checked ambiguous hits (same-named members like Serenity's `IRow.CreateNew`, renode's own `Recorder`, commanddotnet's own `GetDisplayName`, `System.Dynamic` types).

**Sample sizes.** 52 repo-level dependents cloned: FastMember 16, Dynamitey 13, Namotion.Reflection 9, DotNext/DotNext.Reflection 14. All commits pinned in the link index.

**Biases.**

1. **Ecosystem bias is structural, not sampling noise.** Namotion.Reflection's 333M downloads come almost entirely from NJsonSchema/NSwag ([NR-NuGet pkgs]); Dynamitey's from same-author ImpromptuInterface + SpecFlow/Reqnroll plugins; DotNext.Reflection's 137.9K is near-zero next to FastMember's 67M. The "daily use" profile of each library is mostly its biggest dependent's profile.
2. **Source bias:** nuget.org's GitHub-dependents list only covers repos with scannable manifests; private code, paket/nuget.config-era projects, vendored copies, Azure DevOps/GitLab and `Reference Include`-style consumption are invisible. `gh search code` has index lag and caps.
3. **HEAD-only counting:** call sites were counted at each repo's current HEAD (2026-10); historical usage (e.g., EFCore.BulkExtensions' former shipping use of FastMember, ImpromptuInterface's former `Invoke*` forwarding surface) is invisible to this method.
4. **Counting error bars:** method-name counting misses calls routed through local wrappers (Etl.Net hides `TypeAccessor` inside its own `DbDataReader`), dynamic/`dynamic`-typed calls where the member name is a variable, and any calls in non-`.cs` languages (none found — all hits were C#). Same-name false positives were filtered by hand; counts are "order of magnitude", not exact.
5. **Small-n for DotNext.Reflection:** only 4 referencing repos exist in searchable GitHub; combined with nuget.org's "0 dependents" this is evidence of near-absence, not proof of zero.
6. **Self-inclusion:** ImpromptuInterface's Dynamitey call sites are largely its own test suite; reported separately from its shipping code.

## Sources

Library source (API enumeration):

- [FM src]: https://github.com/mgravell/fast-member/tree/e7423b71d98fc0dd1d3a6eebf425fd987d5da56e/FastMember (TypeAccessor.cs, ObjectAccessor.cs, ObjectReader.cs, MemberSet.cs, OrdinalAttribute.cs)
- [DY Dynamic.cs]: https://github.com/ekonbenefits/dynamitey/blob/c44f5c57cb8a075e417b3eca9dc231d9eddaa7f6/Dynamitey/Dynamic.cs
- [DY tree]: https://github.com/ekonbenefits/dynamitey/tree/c44f5c57cb8a075e417b3eca9dc231d9eddaa7f6/Dynamitey
- [DN Reflection dir @HEAD]: https://github.com/dotnet/dotNext/tree/3499aedecf58b3c27a5d87af700d99e6b973cfde/src/DotNext/Reflection
- [DNR tree @refl-4.8]: https://github.com/dotnet/dotNext/tree/24b6f07def13e008a91c4f6455ba2b686d93cf77/src/DotNext.Reflection
- [DN api page]: https://dotnet.github.io/dotNext/api/DotNext.Reflection.html
- [DN#142]: https://github.com/dotnet/dotNext/discussions/142

nuget.org "Used By" / version data (read 2026-10-24):

- [FM-NuGet]: https://www.nuget.org/packages/FastMember/ — 241 pkgs / 26 repos / 67.1M total
- [DY-NuGet]: https://www.nuget.org/packages/Dynamitey/ — 100 pkgs / 11 repos / 35.0M total
- [DY-NuGet pkgs]: same page — ImpromptuInterface 35.1M, SpecFlow.Assist.Dynamic 7.7M, Reqnroll.Assist.Dynamic 1.1M, NBi.Framework.Tools 952.3K, IPTables.Net 505.4K
- [NR-NuGet]: https://www.nuget.org/packages/Namotion.Reflection/ — 76 pkgs / 18 repos / 333.1M total
- [NR-NuGet pkgs]: same page — NJsonSchema 412.1M, NSwag.Core 222.1M, NSwag.Generation 148.3M, NSwag.AspNetCore 144.6M, NSwag.Generation.AspNetCore 137.9M
- [DN-NuGet]: https://www.nuget.org/packages/DotNext/ — 42 pkgs / 6 repos / 11.1M total
- [DNR-NuGet]: https://www.nuget.org/packages/DotNext.Reflection/ — 0 pkgs / 0 repos / 137.9K total, deprecated

fast-member dependent call sites (pinned commits):

- [Serenity L161]: https://github.com/serenity-is/Serenity/blob/e7667d2b828ebea835476d5dfc1e7e3264311005/common-features/src/extensions/Modules/Reporting/DataReport/ExcelReportGenerator.cs#L161 — `TypeAccessor.Create(obj.GetType())`; indexer get at L166; ref in `Serenity.Extensions.csproj`
- [Serenity L166]: same file, L166
- [Etl.Net L18]: https://github.com/paillave/Etl.Net/blob/e31db63002ce24647492becb4571628d4d4b9951/src/Paillave.EntityFrameworkCoreExtension/BulkSave/ObjectDataReader.cs#L18 — `TypeAccessor.Create(type)`; indexer at L25
- [Etl.Net L25]: same file, L25; second (unused) reference: [Etl.Net EFCore csproj L11]: https://github.com/paillave/Etl.Net/blob/e31db63002ce24647492becb4571628d4d4b9951/src/Paillave.Etl.EntityFrameworkCore/Paillave.Etl.EntityFrameworkCore.csproj#L11
- [Tababular L35]: https://github.com/rebus-org/Tababular/blob/dfc7bd74992d7ce9fe02f18a09d4ac6c1864afe5/Tababular/Tababular/Internals/Extractors/ObjectTableExtractor.cs#L35 — `TypeAccessor.Create` via `GetOrAdd`; `GetMembers()` at L75
- [Tababular L75]: same file, L75
- [Debaser L379]: https://github.com/rebus-org/Debaser/blob/8dd0dc7a438f739f294912a3a040b411cea7ea8a/Debaser/Debaser/UpsertHelper.cs#L379 — `TypeAccessor.Create` + `GetMembers` (also ClassMapProperty.cs L48, Postgredebaser UpsertHelper.cs L138/259-260); indexer write: Activator.cs L43
- [Debaser Activator L43]: https://github.com/rebus-org/Debaser/blob/8dd0dc7a438f739f294912a3a040b411cea7ea8a/Debaser/Debaser.Core/Internals/Reflection/Activator.cs#L43
- [Debaser L380]: UpsertHelper.cs L380
- [Topos L77]: https://github.com/mookid8000/Topos/blob/7e766f46e004e47b653753d5c32438e3ba844262/Topos/Topos/Logging/Console/StringRenderer.cs#L77 — `TypeAccessor.Create` + `GetMembers`
- [Topos L78]: same file, L78
- [toadstool L18]: https://github.com/perfectsquircle/toadstool/blob/815dcab941c2de151f030ec4c33d6c8abffe8ffe/src/Toadstool/DataRecordMapper/ClassDataRecordMapper.cs#L18 — `TypeAccessor.Create`, `CreateNew()` L25, `GetMembers()` L46; `CanRead`/`CanWrite` in ReflectionHelper.cs L11-13
- [toadstool L25]: ClassDataRecordMapper.cs L25
- [toadstool L46]: ClassDataRecordMapper.cs L46
- [toadstool L47, ReflectionHelper L14]: ClassDataRecordMapper.cs L47 `m.CanWrite`; https://github.com/perfectsquircle/toadstool/blob/815dcab941c2de151f030ec4c33d6c8abffe8ffe/src/Toadstool/Helpers/ReflectionHelper.cs#L14 `m.CanRead`
- [Dapper.Builder L74]: https://github.com/misha130/Dapper.Builder/blob/f70c0c6e4ef712fdf2fc36a78edb45cd3afd0e92/src/Dapper.Builder/Builder/PropertyParser/PropertyParser.cs#L74 — `TypeAccessor.Create` + `GetMembers` at L36-81
- [Dapper.Builder L82]: same file, L82 — `member.GetAttribute(typeof(IgnoreInsert), false)`
- [Dapper.Builder L37]: same file, L37
- [Dapper.Builder L75]: same file, L75
- [Dapper.Builder L81]: same file, L81
- [WolvenKit CVariable L28]: https://github.com/WolvenKit/WolvenKit-7/blob/7f24ade6af23e622f050428dc63e1a717e1a44aa/WolvenKit.CR2W/Types/Utils/CVariable.cs#L28 — `TypeAccessor.Create(this.GetType())`; indexer get/set L248-588
- [WolvenKit CVariable L248-588]: same file
- [WolvenKit REDReflection L205-217]: https://github.com/WolvenKit/WolvenKit-7/blob/7f24ade6af23e622f050428dc63e1a717e1a44aa/WolvenKit.CR2W/Reflection/REDReflection.cs#L205-L217 — `GetMembers()`, `Member.Ordinal` ordering
- [WolvenKit BufferedClasses L21-36]: https://github.com/WolvenKit/WolvenKit-7/blob/7f24ade6af23e622f050428dc63e1a717e1a44aa/WolvenKit.CR2W/Types/BufferedTypes/BufferedClasses.cs#L21-L36 — `[Ordinal(...)]`
- [WolvenKit csproj L39]: https://github.com/WolvenKit/WolvenKit-7/blob/7f24ade6af23e622f050428dc63e1a717e1a44aa/WolvenKit.CR2W/WolvenKit.CR2W.csproj#L39 — `DotNetHelper.FastMember.Extension` reference
- [AudioBand ObservableObject L22]: https://github.com/AudioBand/AudioBand/blob/668475ec1c7765212d05a92a2ba73b20e847ec3a/src/AudioBand/ObservableObject.cs#L22 — `TypeAccessor.Create(GetType(), true)`; `ObjectAccessor.Create` L90; `GetMembers().Where(m => m.IsDefined(...))` L125
- [AudioBand AudioSourceExtensions L21]: https://github.com/AudioBand/AudioBand/blob/668475ec1c7765212d05a92a2ba73b20e847ec3a/src/AudioSourceHost/AudioSourceExtensions.cs#L21 — `ObjectAccessor.Create`, `typeAccessor.GetMembers()`
- [AudioBand setting L50]: https://github.com/AudioBand/AudioBand/blob/668475ec1c7765212d05a92a2ba73b20e847ec3a/src/AudioSourceHost/AudioSourceSetting.cs#L50 — `_accessor[PropertyName]` get; set L69
- [AudioBand setting L69]: same file, L69
- [WorkloadTools L782]: https://github.com/spaghettidba/WorkloadTools/blob/25ec96fb653653f26ced4181909a933d06bb371c/WorkloadTools/WorkloadTools/Consumer/Analysis/WorkloadAnalyzer.cs#L782 — `ObjectReader.Create(Table, ...)` ×19 columns; also L914
- [WorkloadTools L914]: same file, L914
- [papyruscs L16]: https://github.com/papyrus-mc/papyruscs/blob/fa55835525537164eb06f7662dd8715f12a45c86/PapyrusAlgorithms/Database/DbContextExtensions.cs#L16 — `ObjectReader.Create<T>`; also L43, L80
- [papyruscs L43]: same file, L43
- [papyruscs L80]: same file, L80
- [Dapper test L57]: https://github.com/DapperLib/Dapper/blob/eb47546a408bbf6c178bda4f04dc205bc51ffbfe/tests/Dapper.Tests/SingleRowTests.cs#L57 — `ObjectReader.Create(data)`, test-only
- [Dapper props]: https://github.com/DapperLib/Dapper/blob/eb47546a408bbf6c178bda4f04dc205bc51ffbfe/Directory.Packages.props; [Dapper.Tests csproj]: https://github.com/DapperLib/Dapper/blob/eb47546a408bbf6c178bda4f04dc205bc51ffbfe/tests/Dapper.Tests/Dapper.Tests.csproj
- [EFCore.BulkExtensions test L1552]: https://github.com/borisdj/EFCore.BulkExtensions/blob/0629beb741cbfe45648ef181d992902a2be785f0/EFCore.BulkExtensions.Tests/EFCoreBulkTestAtypical.cs#L1552 — `ObjectReader.Create`, test-only
- [EFBE test csproj]: https://github.com/borisdj/EFCore.BulkExtensions/blob/0629beb741cbfe45648ef181d992902a2be785f0/EFCore.BulkExtensions.Tests/EFCore.BulkExtensions.Tests.csproj#L14
- [XamlCSS tests L92]: https://github.com/warappa/XamlCSS/blob/20c776cd9245cc725a1e5502a9b75894d68addf5/XamlCSS.Tests/Utils/FastReflectionTests.cs#L92 — `TypeAccessor.Create` + indexer set L187, test-only
- [XamlCSS test L187]: same file, L187
- [SMA csproj]: https://github.com/supermemo/SuperMemoAssistant/blob/82ade9699f82420532b68f78b3a51263f7e82e3d/src/Core/SuperMemoAssistant/SuperMemoAssistant.csproj

Dynamitey dependent call sites:

- [Reqnroll L101]: https://github.com/reqnroll/Reqnroll/blob/488afb3735dcfb55f8ec60e929536e4f74b66a3c/Plugins/Reqnroll.Assist.Dynamic/Reqnroll.Assist.Dynamic/DynamicTableHelpers.cs#L101 — `Dynamic.GetMemberNames`, `Dynamic.InvokeGet` (L111, L158, L179); csproj refs Dynamitey 3.0.3
- [Reqnroll L158]: same file, L158
- [Reqnroll L179]: same file, L179
- [SpecFlow L106-184]: https://github.com/marcusoftnet/SpecFlow.Assist.Dynamic/blob/7aaf7738ff8bcb6c38dc73c8d7222356d7191fad/SpecFlow.Assist.Dynamic/DynamicTableHelpers.cs#L106-L184 — identical `GetMemberNames`/`InvokeGet` profile, Dynamitey 2.0.9.136
- [EFCore.Sharding Extention.Object L127-146]: https://github.com/Coldairarrow/EFCore.Sharding/blob/8763ccd9884ad65e188787ba61d3e85ea269d01c/src/EFCore.Sharding/Util/Extention.Object.cs#L127-L146 — `InvokeGet`/`InvokeSet`/`InvokeMember`/`InvokeMemberAction`; also ShardingHelper.cs L58
- [IPTables L42]: https://github.com/splitice/IPTables.Net/blob/6d24569e0b78e15a445051ef608c5188cc3026e9/IPTables.Net/Iptables/IpTablesRule.cs#L42 — `Dynamic.GetMemberNames`, `Dynamic.InvokeGet` L80-81
- [renode DeviceHelper L26]: https://github.com/renode/renode-infrastructure/blob/32dce7d28bc267761b3ef0e49eaf1720be54b265/src/Emulator/Extensions/UserInterface/DeviceHelper.cs#L26 — `InvokeGetIndex`/`InvokeSetIndex`/`InvokeSet`/`InvokeGet`/`InvokeMember`/`InvokeMemberAction` + `InvokeContext.CreateStatic`/`CreateContext` (L87, L149, L161)
- [renode MonitorCommands L743]: https://github.com/renode/renode-infrastructure/blob/32dce7d28bc267761b3ef0e49eaf1720be54b265/src/Emulator/Extensions/UserInterface/MonitorCommands.cs#L743 — `Dynamic.InvokeConvert` ×2 (L743, L813); more in Utilities/TypeExtensions.cs L119-154
- [renode TypeExtensions L119-154]: https://github.com/renode/renode-infrastructure/blob/32dce7d28bc267761b3ef0e49eaf1720be54b265/src/Emulator/Extensions/Utilities/TypeExtensions.cs#L119-L154
- [azure-iot L152]: https://github.com/Azure/azure-iot-remote-monitoring/blob/7baee6a281facc9f332bf13daf7603c801143f8c/Common/Helpers/ReflectionHelper.cs#L152 — `Dynamic.GetMemberNames(item, true)` + `Dynamic.InvokeGet` (L168, L470-474); repo archived 2018
- [RCaron binder L134-137]: https://github.com/Jan0660/RCaron/blob/633ece5615557ddb1226f7c5f19b2b987a495f0d/RCaron.Jit/Binders/RCaronInvokeMemberBinder.cs#L134-L137 — `CacheableInvocation.CreateCall` + `InvocationKind` + `InvokeContext.CreateStatic`
- [RCaron Motor.cs L766-783]: https://github.com/Jan0660/RCaron/blob/633ece5615557ddb1226f7c5f19b2b987a495f0d/RCaron/Motor.cs#L766-L783 — `InvokeContext.CreateStatic`, `InvokeMember`/`InvokeMemberAction`; `InvokeConvert` in BaseLibrary/LoggingModule.cs L141
- [Agency Agent.cs L73-79]: https://github.com/UlyssesWu/Agency/blob/9daca742b37b87b7ee196769ac0c5060855d7692/Agency/Agent.cs#L73-L79 — `Dynamic.Curry` + `Dynamic.CoerceToDelegate`
- [Agency RemoteInvocation L184]: https://github.com/UlyssesWu/Agency/blob/9daca742b37b87b7ee196769ac0c5060855d7692/Agency/RemoteInvocation.cs#L184 — `Dynamic.Invoke` (L184, L192); InvokeMember/InvokeMemberAction/InvokeSetIndex/InvokeGetIndex/InvokeIsEvent/InvokeSubtractAssignMember elsewhere in the demo
- [CoFlows Runtime.cs L564]: https://github.com/CoFlows/CoFlows-CE/blob/6e79f171e2b0a2e75450ab8fe039848ab686fb80/QuantApp.Kernel/JVM/Runtime.cs#L564 — `Dynamic.InvokeMember` (JVM bridge); `InvokeSet` L1275, `InvokeGet` L1332
- [MLOps L39]: https://github.com/aslotte/MLOps.NET/blob/0c792dbafec6b92a06cb21c3a8b62fc580d4d91b/src/MLOps.NET/Catalogs/TrainingCatalog.cs#L39 — `Dynamic.InvokeGet`; vestigial `using Dynamitey.DynamicObjects` in LifeCycleCatalog.cs L1
- [MLOps L1]: https://github.com/aslotte/MLOps.NET/blob/0c792dbafec6b92a06cb21c3a8b62fc580d4d91b/src/MLOps.NET/Catalogs/LifeCycleCatalog.cs#L1
- [impromptu Util.cs L74-83]: https://github.com/ekonbenefits/impromptu-interface/blob/d3b840ba38dc4cdb470e2d83e5db556107ec608c/ImpromptuInterface/src/Optimization/Util.cs#L74-L83 — `InvokeContext` inspection
- [impromptu ActLikeProxy.cs L99-110]: https://github.com/ekonbenefits/impromptu-interface/blob/d3b840ba38dc4cdb470e2d83e5db556107ec608c/ImpromptuInterface/src/EmitProxy/ActLikeProxy.cs#L99-L110 — `AggreType`/`PropretySpecType`; broad `Dynamic.*` coverage in Tests/UnitTestImpromptuInterface (e.g., SingleMethodInvoke.cs L735 `Dynamic.CreateCallSite`, Linq.cs L45-96 `Dynamic.Linq`)
- [NBi csproj]: https://github.com/Seddryck/NBi/blob/5946c75702320267339d6f9d61a821d1f34435f7/NBi.NUnit.Runtime/NBi.NUnit.Runtime.csproj — reference, zero call sites
- [Shoko ClientFactory L155]: https://github.com/ShokoAnime/ShokoDesktop/blob/f1990b21100f8f60e644bca76cf7996a45eee143/Nancy.Rest.Client/ClientFactory.cs#L155 — `Impromptu.ActLike<T>`; dead `using Dynamitey` L12

Namotion.Reflection dependent call sites:

- [NJS JsonSchemaGenerator L76-976]: https://github.com/RicoSuter/NJsonSchema/blob/18ba2ccfd20d795033d00b5e01585a94e2b78486/src/NJsonSchema/Generation/JsonSchemaGenerator.cs#L76 — `ToContextualType`, `.GenericArguments`, `.Nullability`, `.AccessorType` (L607-805), `GetContextualProperties` L798
- [NJS JsonSchemaGenerator L798]: same file, L798
- [NJS JsonReferenceResolver L23]: https://github.com/RicoSuter/NJsonSchema/blob/18ba2ccfd20d795033d00b5e01585a94e2b78486/src/NJsonSchema/JsonReferenceResolver.cs#L23 — `GetContextualAccessors()` + `ContextualAccessorInfo` (also L326)
- [NJS JsonSchema.cs L28]: https://github.com/RicoSuter/NJsonSchema/blob/18ba2ccfd20d795033d00b5e01585a94e2b78486/src/NJsonSchema/JsonSchema.cs#L28
- [NJS TypeExtensions L75-123]: https://github.com/RicoSuter/NJsonSchema/blob/18ba2ccfd20d795033d00b5e01585a94e2b78486/src/NJsonSchema/Infrastructure/TypeExtensions.cs#L75-L123 — `GetXmlDocsSummary`, `GetXmlDocs` (param), `MemberInfo`
- [NJS XmlDocsSettingsExtensions L23-25]: https://github.com/RicoSuter/NJsonSchema/blob/18ba2ccfd20d795033d00b5e01585a94e2b78486/src/NJsonSchema/Generation/XmlDocsSettingsExtensions.cs#L23-L25 — `XmlDocsOptions`
- [NJS JsonSchemaGenerator L473]: https://github.com/RicoSuter/NJsonSchema/blob/18ba2ccfd20d795033d00b5e01585a94e2b78486/src/NJsonSchema/Generation/JsonSchemaGenerator.cs#L473 — `GetXmlDocsTag` (also L493, L884)
- [NSwag OperationResponseProcessorBase L106]: https://github.com/RicoSuter/NSwag/blob/63daf8fcc3a25151b62eb4b326a1e8ea048a0d41/src/NSwag.Generation/Processors/OperationResponseProcessorBase.cs#L106 — `GetXmlDocsElement`
- [NSwag OperationParameterProcessor L430]: https://github.com/RicoSuter/NSwag/blob/63daf8fcc3a25151b62eb4b326a1e8ea048a0d41/src/NSwag.Generation.WebApi/Processors/OperationParameterProcessor.cs#L430 — `GetContextualProperties` on contextual parameter type
- [NSwag OpenApiDocumentMiddleware L106-107]: https://github.com/RicoSuter/NSwag/blob/63daf8fcc3a25151b62eb4b326a1e8ea048a0d41/src/NSwag.AspNetCore/Middlewares/OpenApiDocumentMiddleware.cs#L106-L107 — `XmlDocs.ClearCache()` + `CachedType.ClearCache()`
- [saunter L222-283]: https://github.com/asyncapi/saunter/blob/e5b9d7b7cbb4fd3549282ac544e7560e4dbca0b5/src/Saunter/AttributeProvider/AttributeDocumentProvider.cs#L222-L283 — `GetXmlDocsRemarks`/`GetXmlDocsSummary` only
- [SMB L164-229]: https://github.com/zarusz/SlimMessageBus/blob/7d40db25873c01e33def0dae270c53cdf1b25df0/src/SlimMessageBus.Host.AsyncApi/MessageBusDocumentGenerator.cs#L164-L229 — `GetXmlDocsSummary`/`GetXmlDocsRemarks` only
- [CD ResourceGenerators L112]: https://github.com/bilal-fazlani/commanddotnet/blob/0a413721d2705917a4e4c3581c5e43aa79c0fb84/CommandDotNet.Tests/UnitTests/Localization/ResourceGenerators.cs#L112 — `member.GetXmlDocsSummary()`, test project only; [CD test csproj L11]: https://github.com/bilal-fazlani/commanddotnet/blob/0a413721d2705917a4e4c3581c5e43aa79c0fb84/CommandDotNet.Tests/CommandDotNet.Tests.csproj#L11
- [TypeGen L108-109]: https://github.com/jburzynski/TypeGen/blob/f741b09ea981a2ea150f58834b69a249f377a351/src/TypeGen/TypeGen.Core/Extensions/TypeExtensions.cs#L108-L109 — `ToContextualAccessor()` + `.Nullability` (Namotion.Reflection 2.1.1)

DotNext / DotNext.Reflection dependent call sites:

- [KurrentDB InMemoryBus.MessageHierarchy.cs L91]: https://github.com/kurrent-io/KurrentDB/blob/bdc4964b4fb11c8db1cf4772245f102bfae359e8/src/KurrentDB.Core/Bus/InMemoryBus.MessageHierarchy.cs#L91 — `messageType.GetBaseTypes()`; devirtualize comment L110
- [KurrentDB ErrorExtensions.cs L58]: https://github.com/kurrent-io/KurrentDB/blob/bdc4964b4fb11c8db1cf4772245f102bfae359e8/src/KurrentDB.Api.V2/Infrastructure/Errors/ErrorExtensions.cs#L58 — `errorCode.GetCustomAttribute<T, OriginalNameAttribute>()` (`EnumType`)
- [TrogonEventStore L111]: https://github.com/TrogonStack/TrogonEventStore/blob/d9a00b8cc40080832100bd596731561111bd01e3/src/EventStore.Core/Bus/InMemoryBus.MessageHierarchy.cs#L111 — `GetBaseTypes()` (forked same code)
- [Craftimizer Ipc.cs L46]: https://github.com/WorkingRobot/Craftimizer/blob/1d4a2c67ea47a25d68701e8e69c8744dbbd56d86/Craftimizer/Utils/Ipc.cs#L46 — `typeMethod.GetParameterTypes()`
- [Serifu.org JsonArrayToRecordConverter.cs L34]: https://github.com/maxkagamine/Serifu.org/blob/5fa87131cfb9511cdc9c8d25c3b15233b898f2fb/Serifu.Importer.Generic/Kirikiri/JsonArrayToRecordConverter.cs#L34 — `ctor.GetParameterTypes()`; no DotNext reference in the project file — arrives transitively
- [LightBlog getter L29-71]: https://github.com/calanchenlins/LightBlog/blob/81b9aa57bd71defe08876dbe8abecf126084d74e/performance/KaneBlake.Basis.Performance/Benchmarks/ReflectionGetterBenchmark.cs#L29-L71 — `Type<T>.Property<V>.RequireGetter`, `MemberGetter`, `DynamicInvoker`
- [LightBlog setter L29-35]: https://github.com/calanchenlins/LightBlog/blob/81b9aa57bd71defe08876dbe8abecf126084d74e/performance/KaneBlake.Basis.Performance/Benchmarks/ReflectionSetterBenchmark.cs#L29-L35 — `RequireSetter`, `MemberSetter`
- [datatent2 L6-13]: https://github.com/sebfischer83/datatent2/blob/327a4cadf2e2ffebe179a220a25062e209cee9f9/src/Datatent2.Core/Services/Metadata/ObjectMetadataService.cs#L6-L13 — `using DotNext.Reflection` on an empty class; package 4.3.0 in Datatent2.Core.csproj L32
- [BD csproj L29]: https://github.com/BeyondDimension/Common/blob/cafaaf981a7ba9faa800eb188de1956f4fbc9c04/src/Ipc.Client.Sample.Experimental/Ipc.Client.Sample.Experimental.csproj#L29 — `<Using Include="DotNext.Reflection" />`
- [KurrentDB]: https://github.com/kurrent-io/KurrentDB — aggregate `DotNext.*` namespace counts by ripgrep over `src/`

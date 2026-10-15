# Strong-name guidance for a net462 / netstandard2.0 / net8.0 library, and what changing assembly identity after 1.0 costs consumers

Research note for GitHub issue Skymly/FastReflection#5 (feeds the strong naming ticket). Date: 2026-10-16. Evidence only; no decision is made or recommended here.

## Question

What is Microsoft's guidance for strong-naming a library that targets net462, netstandard2.0 and net8.0, and what does it cost consumers if the strong-name key, assembly version or identity changes after 1.0? How do comparable libraries handle it?

## Short answer

Microsoft's current guidance: if a library's targets include .NET Framework or .NET Standard, strong-name it on every target framework; keep the key pair in source control (or commit the public key and public-sign); never add, remove or change the key; consider changing AssemblyVersion only on major releases; never publish signed and unsigned twin packages. Landscape §6.6's "sign the assembly directly" is consistent; its parenthetical "or ship a Signed variant package" contradicts the explicit DO NOT, present since 2018. An identity change costs .NET Framework consumers a runtime `FileLoadException` (reproduced here); .NET 8 showed no failure. `Skymly.FastReflection` has no versions on nuget.org yet (404), so no published identity exists to preserve. Signing also forces a signed test assembly and `Key=` on InternalsVisibleTo.

## Findings

### 1. What Microsoft's guidance says

Sources: the dotnet/docs pages behind Learn (pinned at commit `a3fc8c2`, read 2026-10-16) and two dotnet/runtime docs. Learn pages: [strong-naming](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/strong-naming), [versioning](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning), [breaking changes](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes).

**Whether to sign.**
- Not required for .NET Core/5+-only libraries; "You should strong name your open-source .NET libraries if their targets include .NET Framework or .NET Standard", and when multi-targeting those plus modern .NET, "strong name across all of your Target Frameworks" ([SN L40-49]). For net462;netstandard2.0;net8.0 that covers all three. This scoping dates from 2025-10-13; earlier revisions said to sign open-source libraries generally ([SN history]).
- Reason: on .NET Framework strong naming is "viral" (a strong-named assembly can reference only strong-named ones), so an unsigned library excludes strong-named consumers ([SN L26-28], [RT-SN L17]). On .NET Core/5+ the runtime "never validates the strong-name signature, nor does it use the strong-name for assembly binding" ([SNA L37]). Strong names are identity, not security; publisher identity is Authenticode or NuGet signing ([SNA L23], [SN L59-60]).

**Key stability.** "DO NOT add, remove, or change the strong naming key": it "changes the assembly's identity and breaks compiled code that uses it" ([SN L66-68]); the breaking-changes page adds "DO NOT change an assembly name" ([BRK L61-65]). dotnet/runtime: removing a strong name "is a breaking change" ([RT-SN L6]). Upgrading key size "breaks all binaries that reference the older identity"; .NET Framework 4.5+ enhanced strong naming keeps the identity key and upgrades only the signature key ([ESN L20-36]). The key rule is in every revision since 2018-10-16 and the twin rule in every revision since the page's first commit, 2018-10-08 ([SN history]).

**Key storage and public signing.** CONSIDER committing the key pair; if you can't, commit the public key and public-sign regular builds ([SN L51-57]). Public signing "applies a public key but doesn't actually sign the assembly" yet sets the signed bit ([PS L29-35]); `KeyContainer` is unsupported on CoreCLR compilers, so use `KeyFile` ([PS L67]). dotnet/runtime: on .NET Framework public-signed assemblies are supported "only ... for debugging and testing"; they cannot load in shadow-copy or partially trusted AppDomains, the GAC or NGen; on .NET Core they are supported for production ([RT-PS L8-19]). Since .NET Framework 3.5 SP1, full-trust loads skip signature validation ([BYP L12]); identity is still enforced (E1).

**Version numbers.** The package version is what users see and has no runtime effect ([VER L25]). AssemblyVersion is what the CLR uses to select assemblies, and only for strong-named assemblies; .NET Framework demands an exact match, .NET (Core) "automatically loads assemblies with a higher version" ([VER L45-53], [AV L53], [LOC L58]). CONSIDER a major-only AssemblyVersion, major kept in sync with the package; "DO NOT have a fixed AssemblyVersion" ([VER L55-63], [SN L62]). FileVersion has no runtime effect (CONSIDER a CI build number as revision; use Major.Minor.Build.Revision); leave InformationalVersion to SourceLink ([VER L77-96]).

**Binding redirects.** .NET Framework: strict loading, so strong-named references need redirects ([SN L11]). Assembly unification redirects only .NET Framework's own assemblies to the runtime's version; third-party assemblies need manual redirects ([RED L24-28]). Apps get automatic redirects, but test projects need `GenerateBindingRedirectsOutputType` and plugin hosts may ignore `.dll.config` ([RED L67-77], [RED L93-110]). .NET 5+/Core: no strict loading ([SN L26]); a loaded assembly satisfies a reference if its version "is equal or higher" ([ALC L24]).

**InternalsVisibleTo and tests.** A strong-named assembly's friend must itself be strong-named and be named with the full public key, not token or version ([FR L74-76]); otherwise CS1726 ([CS1726]). The SDK `InternalsVisibleTo` item takes `Key` metadata ([SDK L1866]).

**Satellite packages on a signed core.** No dedicated guidance found in the pages reviewed. Inferred from the rules above: a satellite used by strong-named apps must be signed (viral); a core that shares internals must name the satellite's full public key; a satellite compiled against the core pins the core's PublicKeyToken and AssemblyVersion (E1).

### 2. The §6.6 claim

`docs/research/2026-10-12-fast-reflection-landscape.md` L275 (main @ `7fcfffa`, translated from Chinese): follow the `FastMember.Signed` pattern, "sign the assembly directly (or ship a Signed variant package)". L280 expects `InternalsVisibleTo("FastReflection_dynamic, PublicKey=...")`.

- Guidance: "DO NOT publish strong-named and non-strong-named versions of your library. For example, `Contoso.Api` and `Contoso.Api.StrongNamed`", because it "forks your developer eco-system" and can cause "type name conflicts" ([SN L70-72]).
- Verdict by part: "sign the assembly directly" matches [SN L40-49]. "Ship a Signed variant package" contradicts the DO NOT. "The FastMember.Signed pattern" is itself the twin: `FastMember` is unsigned and `FastMember.Signed` is a separate package whose assembly is named `FastMember.Signed` (section 4).
- Reproduced: referencing both twins fails with CS0433 (E2).
- Maintainers' experience: Dapper's maintainers called signing "fundamentally identity breaking" and planned to fold the twin away at a major bump (2019); 2.1.89 (2026-09) still ships both. A user reports extension packages referencing `Dapper` conflict with `Dapper.StrongName` ("classes existing in multiple assemblies") ([DP#1356]). In [FM#108] a participant says the twin "fractured the ecosystem"; the maintainer's reply discusses code-signing certificates; the issue is open. StackExchange.Redis retired its twin: "The StackExchange.Redis package is now strong-named; StackExchange.Redis.StrongName is no longer released separately" ([REG-SER]).
- What a twin avoids: changing the identity of an already-published unsigned package (E1, row 2). `Skymly.FastReflection` has no published versions ([FR-404], [FR-SEARCH]).
- Dynamic-assembly friend: `src` creates only `DynamicMethod`s with `skipVisibility: true` ([EmitHelper L43-47]) and has no `TypeBuilder`/`AssemblyBuilder`/`ModuleBuilder` (0 hits), so no friend dynamic assembly exists today (inferred).

### 3. First-hand experiments (scratch projects; not Microsoft guarantees)

Setup: SDK 10.0.303; library `Lib` (netstandard2.0) in six variants (unsigned, throwaway key A, key B; AssemblyVersion 1.0.0.0 or 2.0.0.0). A consumer exe (net48 on .NET Framework 4.8.x, Release 533509; net8.0 on .NET 8.0.x) was compiled against one variant, then `Lib.dll` was swapped. App-local DLL, full trust. Errors appear at first use, not at build. HRESULT 0x80131040 = "located assembly's manifest definition does not match the assembly reference"; 0x80131044 = "A strongly-named assembly is required".

**E1: compiled against → loaded at run time**

| Compiled against → loaded | .NET Framework 4.8.x | .NET 8 |
|---|---|---|
| unsigned 1.0.0.0 → unsigned 2.0.0.0 | OK | OK |
| unsigned 1.0.0.0 → signed (key added) | `FileLoadException` 0x80131040 | OK |
| key A 1.0.0.0 → key A 2.0.0.0 | 0x80131040; OK with a `bindingRedirect` | OK |
| key A 1.0.0.0 → key B (any version) | 0x80131040, also with the redirect | OK |
| key A 1.0.0.0 → unsigned (key removed) | 0x80131040 | OK |
| signed consumer built against unsigned `Lib` (CS8002 suppressed) | 0x80131044 whatever `Lib` is loaded | OK |

**E2:** FastMember 1.5.0 + FastMember.Signed 1.5.0, and Dapper 2.1.89 + Dapper.StrongName 2.1.89, referenced together: `error CS0433: The type 'TypeAccessor' exists in both 'FastMember.Signed, ... PublicKeyToken=9e8f22703bef9a29' and 'FastMember, ... PublicKeyToken=null'`; `SqlMapper` likewise.

**E3:** signed `Lib` with `<InternalsVisibleTo Include="Lib.Tests" />`: CS1726 whether the tests are signed or not. With `Key=` and signed tests: builds. With `Key=` and unsigned tests: CS0281. Unsigned `Lib` with unsigned tests (today's FastReflection layout): builds.

**E4:** with no explicit AssemblyVersion the SDK derives it from `<Version>`: 0.1.0 gives 0.1.0.0; 1.2.3-beta.4 gives 1.2.3.0.

### 4. Comparable libraries

Method: nupkgs from the NuGet flat container (pattern [NUPKG]) read with `AssemblyName.GetAssemblyName`, strong-name flag and signature blob read from the PE header; tokens recomputed from committed `.snk` files; project files at pinned commits.

| Library | Signing | Key pair committed? | AssemblyVersion | Notes |
|---|---|---|---|---|
| Newtonsoft.Json 13.0.4 | Signed, all 8 TFMs of 13.0.3; token `30ad4fe6b2a6aeed` unchanged 6.0.8 to 13.0.4 | No: the build reads an external path ([NJ-BUILD L12-13]); the committed `Dynamic.snk` has token `b9a188c8922137c6`, the key of the `Newtonsoft.Json.Dynamic` friend ([NJ-IVT L54]) | major only (6.0.0.0 ... 13.0.0.0); FileVersion carries the full version ([NJ-BUILD L19], [NJ-BUILD L203-204]) | IVT switches on `SIGNED`: simple names unsigned, `PublicKey=` signed ([NJ-IVT L45-52]). The Microsoft page lists it as committing its key pair ([SN L78]); not corroborated |
| Dapper 2.1.89 | `Dapper` unsigned; `Dapper.StrongName` twin, own assembly name, token `e3e8412083d25dd3` | Yes (`Dapper.snk`; token matches the twin) | pinned `2.0.0.0` ([DP-VER L4]) | Twin recompiles `..\Dapper\**\*.cs` with `STRONG_NAME` ([DP-SN L9-16]); tests' IVT dropped in the twin ([DP-IVT L2-4]); satellite twin ([DP-EF L9-18]); Contrib twin requested ([DP#1745]) |
| FastMember 1.5.0 / FastMember.Signed 1.5.0 | `FastMember` unsigned; `FastMember.Signed` separate ID, assembly `FastMember.Signed`, token `9e8f22703bef9a29` | Yes (`FastMember.snk`) | package version (1.5.0.0); major-only requested ([FM#76], open since 2019) | `PublicSign` when `$(OS)` is not Windows_NT ([FM-SIGNED L15-19]); no IVT; last release 2019-05-24, not deprecated |
| Namotion.Reflection 3.5.1 | Signed on net462, netstandard2.0, net8.0 ([NR-CSPROJ L3-5]); token `c2f9c3bdfae56102` unchanged 2.0.1 to 3.5.1 | Yes (`src/Namotion.Reflection.snk`) | package version (3.5.1.0) | Signed satellite `Namotion.Reflection.Cecil` uses the same key ([NR-CECIL L3-5]); the core grants it IVT by full `PublicKey=` ([NR-IVT L23]); tests get no IVT |
| DotNext 6.10.0 / DotNext.Reflection 4.9.0 | Signed in the Release configuration only ([DN-CSPROJ L35-39]); one token `c8827b44e18fe572` for both (3.3.0 to 4.9.0, 5.26.3 to 6.10.0) | Yes (`src/dotnext.snk`) | package version | Modern .NET only (net10.0; Reflection 4.x net6.0), so .NET Framework strict binding does not arise. In `DotNext.Net.Cluster`, test IVT (simple names) exists only under `DEBUG` or `PUBLISH_AOT` ([DN-IVT L2-7]); signing is Release-only and off for AOT ([DNC-CSPROJ L44-56]). DotNext.Reflection is deprecated, reason "Legacy" ([REG-DNR]) |
| Dynamitey 3.0.3 / Dynamitey.Community 4.0.0 | Original signed ([DY-CSPROJ L16-18]); token `cbf53ea3aeb972c6` unchanged 1.0.2 to 3.0.3. Fork: new assembly name, new 2048-bit key `0e3788a96f882aea` | Yes, both | package version (fork 4.0.0.0) | Fork's reasons: [DYC-ID L6-22], [DYC-KEY L63-75], [DYC#3] |

Every inspected signed assembly except Dynamitey.Community carries a 160-byte (1024-bit RSA) public key.

**Identity-change incidents**
- FastMember.Signed changed identity inside one package ID: 1.0.0.10 and 1.0.0.11 shipped assembly `FastMember` (token `9056fda458ff61cf`); 1.1.0 shipped `FastMember.Signed` (token `9e8f22703bef9a29`); 1.2.0 shipped AssemblyVersion `0.0.0.0` ([FM#49]), breaking ClosedXML 0.91.0 with 0x80131040 ([FM#50]).
- Dapper.StrongName 2.1.86 was public-signed by mistake; a .NET Framework 4.8 web app got 0x80131045 "Strong name signature could not be verified"; fixed in 2.1.89 ([DP#2236]).
- Dynamitey.Community kept signing because "a strong-named assembly may only reference other strong-named assemblies ... Dropping it would break them on upgrade" ([DYC-KEY]); it moved the assembly name to avoid a "coin flip that surfaces as a runtime MissingMethodException" ([DYC-ID L6-22]).

## Caveats and unknowns

- E1 to E4: one machine, one SDK, .NET Framework 4.8.x and .NET 8 only; app-local DLL, full trust. Not tested: GAC, publisher policy, shadow copy, partial trust, plugin load contexts, a lower-than-requested version on .NET 8, Mono, Unity.
- How many FastReflection consumers are strong-named .NET Framework apps: not found.
- Newtonsoft.Json: the repo at the pinned commit contains no key matching the shipped token; whether an older commit did was not checked.
- Signature validity was not verified with `sn.exe`; for Dapper.StrongName 2.1.89 I saw the signed flag and a non-zero signature blob only.
- DotNext.Reflection source is not at HEAD; only package metadata was inspected. The Microsoft strong-naming page front matter says `ai-usage: ai-assisted` ([SN L5]); the key and twin rules predate it (history scan).
- Dapper#1356, FastMember and Dynamitey statements are maintainers' own words, not independently verified.
- Microsoft pages reviewed contain no satellite-specific guidance; no search beyond library-guidance, assembly and framework docs.

## Implications for the decision ticket

- The TFM set includes .NET Framework and .NET Standard, so the guidance's "sign across all target frameworks" and the twin prohibition both apply as written.
- The first publish fixes the identity. Adding a key later is E1's second row: .NET Framework consumers compiled against the unsigned build fail at run time. That is the cost Dapper's maintainers cite for the twin ([DP#1356]); with no published version it does not yet exist here.
- Any later change of key, assembly name ([BRK L61-65]) or (without redirects) AssemblyVersion breaks .NET Framework consumers; .NET 8 consumers were unaffected, so the cost concentrates on the net462/netstandard2.0-on-Framework audience that §6.6 targets.
- Today `FastReflection.csproj` sets `<Version>` only ([csproj L8]), so AssemblyVersion tracks every package version (E4). Signed, each bump then needs redirects on .NET Framework (E1, row 3); Newtonsoft.Json and Dapper pin the major, Namotion.Reflection, DotNext, Dynamitey and FastMember.Signed do not.
- Signing changes this repo: `<InternalsVisibleTo Include="FastReflection.Tests" />` ([csproj L42]) would fail with CS1726 (E3); the test project ([tests csproj]) must be signed and the item given `Key=`; CI needs the key or committed `.snk` (`.gitignore` already leaves `*.snk` un-ignored, L251-253).
- A twin multiplies package IDs and satellites (Dapper has `Dapper.EntityFramework.StrongName`, a Contrib twin is requested) and makes CS0433 possible for consumers (E2).
- Public signing is supported for production only on .NET Core; on .NET Framework it is "debugging and testing" ([RT-PS L10]).

## Link index

[SN L5]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L5
[SN L11]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L11
[SN L26-28]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L26-L28
[SN L26]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L26
[SN L40-49]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L40-L49
[SN L51-57]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L51-L57
[SN L59-60]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L59-L60
[SN L62]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L62
[SN L66-68]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L66-L68
[SN L70-72]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L70-L72
[SN L78]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/strong-naming.md#L78
[SN history]: https://github.com/dotnet/docs/commits/main/docs/standard/library-guidance/strong-naming.md
[BRK L61-65]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/breaking-changes.md#L61-L65
[VER L25]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/versioning.md#L25
[VER L45-53]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/versioning.md#L45-L53
[VER L55-63]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/versioning.md#L55-L63
[VER L77-96]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/library-guidance/versioning.md#L77-L96
[SNA L23]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/assembly/strong-named.md#L23
[SNA L37]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/assembly/strong-named.md#L37
[AV L53]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/assembly/versioning.md#L53
[LOC L58]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/framework/deployment/how-the-runtime-locates-assemblies.md#L58
[ALC L24]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/core/dependency-loading/understanding-assemblyloadcontext.md#L24
[RED L24-28]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/framework/configure-apps/redirect-assembly-versions.md#L24-L28
[RED L67-77]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/framework/configure-apps/redirect-assembly-versions.md#L67-L77
[RED L93-110]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/framework/configure-apps/redirect-assembly-versions.md#L93-L110
[FR L74-76]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/assembly/friend.md#L74-L76
[CS1726]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/csharp/language-reference/compiler-messages/cs1726.md
[SDK L1866]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/core/project-sdk/msbuild-props.md#L1866
[PS L29-35]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/csharp/language-reference/compiler-options/security.md#L29-L35
[PS L67]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/csharp/language-reference/compiler-options/security.md#L67
[ESN L20-36]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/assembly/enhanced-strong-naming.md#L20-L36
[BYP L12]: https://github.com/dotnet/docs/blob/a3fc8c2b42595c84b5b3d98679596d75aa6c9431/docs/standard/assembly/disable-strong-name-bypass-feature.md#L12
[RT-SN L6]: https://github.com/dotnet/runtime/blob/2165605a2fd24cd9bfb641143cb03cb19e29dc32/docs/project/strong-name-signing.md#L6
[RT-SN L17]: https://github.com/dotnet/runtime/blob/2165605a2fd24cd9bfb641143cb03cb19e29dc32/docs/project/strong-name-signing.md#L17
[RT-PS L8-19]: https://github.com/dotnet/runtime/blob/2165605a2fd24cd9bfb641143cb03cb19e29dc32/docs/project/public-signing.md#L8-L19
[RT-PS L10]: https://github.com/dotnet/runtime/blob/2165605a2fd24cd9bfb641143cb03cb19e29dc32/docs/project/public-signing.md#L10
[NUPKG]: https://api.nuget.org/v3-flatcontainer/dapper.strongname/2.1.89/dapper.strongname.2.1.89.nupkg
[FR-404]: https://api.nuget.org/v3-flatcontainer/skymly.fastreflection/index.json
[FR-SEARCH]: https://azuresearch-usnc.nuget.org/query?q=packageid:Skymly.FastReflection&prerelease=true
[REG-SER]: https://api.nuget.org/v3/registration5-gz-semver2/stackexchange.redis.strongname/index.json
[REG-DNR]: https://api.nuget.org/v3/registration5-gz-semver2/dotnext.reflection/index.json
[NJ-BUILD L12-13]: https://github.com/JamesNK/Newtonsoft.Json/blob/52fa3aef1f2cadcd3a3f874251eddc98d3efbbaa/Build/build.ps1#L12-L13
[NJ-BUILD L19]: https://github.com/JamesNK/Newtonsoft.Json/blob/52fa3aef1f2cadcd3a3f874251eddc98d3efbbaa/Build/build.ps1#L19
[NJ-BUILD L203-204]: https://github.com/JamesNK/Newtonsoft.Json/blob/52fa3aef1f2cadcd3a3f874251eddc98d3efbbaa/Build/build.ps1#L203-L204
[NJ-IVT L45-52]: https://github.com/JamesNK/Newtonsoft.Json/blob/52fa3aef1f2cadcd3a3f874251eddc98d3efbbaa/Src/Newtonsoft.Json/Properties/AssemblyInfo.cs#L45-L52
[NJ-IVT L54]: https://github.com/JamesNK/Newtonsoft.Json/blob/52fa3aef1f2cadcd3a3f874251eddc98d3efbbaa/Src/Newtonsoft.Json/Properties/AssemblyInfo.cs#L54
[DP-VER L4]: https://github.com/DapperLib/Dapper/blob/eb47546a408bbf6c178bda4f04dc205bc51ffbfe/version.json#L4
[DP-SN L9-16]: https://github.com/DapperLib/Dapper/blob/eb47546a408bbf6c178bda4f04dc205bc51ffbfe/Dapper.StrongName/Dapper.StrongName.csproj#L9-L16
[DP-IVT L2-4]: https://github.com/DapperLib/Dapper/blob/eb47546a408bbf6c178bda4f04dc205bc51ffbfe/Dapper/Global.cs#L2-L4
[DP-EF L9-18]: https://github.com/DapperLib/Dapper/blob/eb47546a408bbf6c178bda4f04dc205bc51ffbfe/Dapper.EntityFramework.StrongName/Dapper.EntityFramework.StrongName.csproj#L9-L18
[DP#1356]: https://github.com/DapperLib/Dapper/issues/1356
[DP#1745]: https://github.com/DapperLib/Dapper/issues/1745
[DP#2236]: https://github.com/DapperLib/Dapper/issues/2236
[FM-SIGNED L15-19]: https://github.com/mgravell/fast-member/blob/e7423b71d98fc0dd1d3a6eebf425fd987d5da56e/FastMember.Signed/FastMember.Signed.csproj#L15-L19
[FM#49]: https://github.com/mgravell/fast-member/issues/49
[FM#50]: https://github.com/mgravell/fast-member/issues/50
[FM#76]: https://github.com/mgravell/fast-member/issues/76
[FM#108]: https://github.com/mgravell/fast-member/issues/108
[NR-CSPROJ L3-5]: https://github.com/RicoSuter/Namotion.Reflection/blob/e8a92a87d7842211ad6d900355601ace4b3a6cd3/src/Namotion.Reflection/Namotion.Reflection.csproj#L3-L5
[NR-IVT L23]: https://github.com/RicoSuter/Namotion.Reflection/blob/e8a92a87d7842211ad6d900355601ace4b3a6cd3/src/Namotion.Reflection/XmlDocsExtensions.cs#L23
[NR-CECIL L3-5]: https://github.com/RicoSuter/Namotion.Reflection/blob/e8a92a87d7842211ad6d900355601ace4b3a6cd3/src/Namotion.Reflection.Cecil/Namotion.Reflection.Cecil.csproj#L3-L5
[DN-CSPROJ L35-39]: https://github.com/dotnet/dotNext/blob/3499aedecf58b3c27a5d87af700d99e6b973cfde/src/DotNext/DotNext.csproj#L35-L39
[DNC-CSPROJ L44-56]: https://github.com/dotnet/dotNext/blob/3499aedecf58b3c27a5d87af700d99e6b973cfde/src/cluster/DotNext.Net.Cluster/DotNext.Net.Cluster.csproj#L44-L56
[DN-IVT L2-7]: https://github.com/dotnet/dotNext/blob/3499aedecf58b3c27a5d87af700d99e6b973cfde/src/cluster/DotNext.Net.Cluster/Assembly.cs#L2-L7
[DY-CSPROJ L16-18]: https://github.com/ekonbenefits/dynamitey/blob/c44f5c57cb8a075e417b3eca9dc231d9eddaa7f6/Dynamitey/Dynamitey.csproj#L16-L18
[DYC-ID L6-22]: https://github.com/dynamitey-community/dynamitey/blob/755b93609fb210db1407d74b6040d96e21d44398/Dynamitey/Dynamitey.csproj#L6-L22
[DYC-KEY L63-75]: https://github.com/dynamitey-community/dynamitey/blob/755b93609fb210db1407d74b6040d96e21d44398/Dynamitey/Dynamitey.csproj#L63-L75
[DYC-KEY]: https://github.com/dynamitey-community/dynamitey/blob/755b93609fb210db1407d74b6040d96e21d44398/Dynamitey/Dynamitey.csproj#L63-L75
[DYC#3]: https://github.com/dynamitey-community/dynamitey/issues/3
[EmitHelper L43-47]: https://github.com/Skymly/FastReflection/blob/7fcfffa0aa2931b2930a04e901c1f71d9d3b5d5c/src/FastReflection/Emit/EmitHelper.cs#L43-L47
[csproj L42]: https://github.com/Skymly/FastReflection/blob/7fcfffa0aa2931b2930a04e901c1f71d9d3b5d5c/src/FastReflection/FastReflection.csproj#L42
[csproj L8]: https://github.com/Skymly/FastReflection/blob/7fcfffa0aa2931b2930a04e901c1f71d9d3b5d5c/src/FastReflection/FastReflection.csproj#L8
[tests csproj]: https://github.com/Skymly/FastReflection/blob/7fcfffa0aa2931b2930a04e901c1f71d9d3b5d5c/tests/FastReflection.Tests/FastReflection.Tests.csproj

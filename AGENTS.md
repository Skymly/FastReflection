# FastReflection — agent notes

## Commands

- Build: `dotnet build FastReflection.slnx` (slnx format; `global.json` pins the 10.0.x SDK band, `rollForward: latestFeature`)
- Test (all legs): `dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj`
- Test single leg: append `-f net472`, `-f net6.0`, `-f net8.0` or `-f net10.0` (net472 leg consumes the `net462` library build, net6.0 the `netstandard2.0` build)
- Coverage: the Nuke `Test`/`Coverage` targets (`./build.cmd Test Coverage`) collect cobertura on the newest modern leg of every `tests/*/*.Tests.csproj` into `artifacts/coverage/` and enforce ≥90% line coverage per shipped assembly on the union report (`--coverage-threshold` to override); collector config in `tests/FastReflection.Tests/coverage.runsettings`
- Pack: `./build.cmd Pack` packs every `src/*/*.csproj` into `artifacts/` and gates ADR-0003 — every packed assembly strong-named, one public key token across all packages
- Benchmarks: `dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -- --job short`
- Benchmark gate: `./build.cmd Benchmark` runs `--job short` per benchmark leg into `artifacts/benchmarks/<tfm>/` and fails when a gated row's mean, normalized by that run's anchor, is >25% slower than `benchmarks/baseline.json` (`--benchmark-tolerance` to override, `--benchmark-filter` for a glob subset, `--benchmark-skip-run` to compare/write against existing artifacts without re-measuring)

## Structure

- `src/FastReflection/` — the core library `Skymly.FastReflection` (assembly/namespace `FastReflection`)
- `src/FastReflection.ObjectReader/`, `src/FastReflection.XmlDocs/` — satellites `Skymly.FastReflection.*` (#34 topology; public API lands in #36/#37). They consume the core through public API only — no inbound `InternalsVisibleTo` — and inherit the shipped TFM set, packaging props and analyzer gates from `src/Directory.Build.props`.
- All shipped projects multi-target `net462;netstandard2.0;net8.0;net10.0` (hoisted in `src/Directory.Build.props`); test matrix `net472;net6.0;net8.0;net10.0`, benchmarks `net472;net8.0;net10.0`.
- Core files:
  - `ReflectionExtensions.cs` — public API surface (CreateGetter/Setter/Invoker/Factory, Unreflect, net8+ Span invokers)
  - `Emit/EmitHelper.cs` — DynamicMethod factory + IL micro-helpers + `DynamicCodeSupported` probe
  - `Emit/AccessorEmitter.cs` — IL emit for accesses and exact binds, plus reflection fallback
  - `TypeAccessor.cs`, `ObjectAccessor.cs`, `Member.cs` — type accessor, object accessor, and Member
  - `Polyfills.cs` — downlevel-TFM polyfills for the linker attribute set (`RequiresDynamicCode`/`RequiresUnreferencedCode`/`UnconditionalSuppressMessage`/`DynamicallyAccessedMembers`/`DynamicDependency` + the `DynamicallyAccessedMemberTypes` enum), name-matched to the BCL shapes (verify `ValidOn` against the net8.0 ref assembly — e.g. RDC/RUC are `Class|Constructor|Method` only)
- `tests/FastReflection.AotSmoke/` — PublishAot smoke app asserting the no-dynamic-code contract end-to-end; the Nuke `AotSmoke` target publishes linux-x64 and runs it, Linux only (invoked via `Test`). Exit 0 pass; nonzero names the failed scenario.

## Conventions / gotchas

- net462/netstandard2.0 have no `MethodInfo.CreateDelegate<T>()` — use `CreateDelegate(Type)` + cast.
- `RuntimeFeature` doesn't exist on net462/netstandard2.0 — dynamic-code probe is `#if`'d per TFM.
- Ref-returning property without setter is writable via getter + `stobj`; that write is an access, not `Member.CanWrite`. `ref readonly` is detected via `InAttribute`/`IsReadOnlyAttribute` modreq and has no write access.
- Struct member writes use `unbox` (managed pointer into the box) so writes persist — not `unbox.any`. See `docs/adr/0001-boxed-value-writes-update-the-box.md`.
- Tests disable xunit parallelization assembly-wide because `FallbackTests` toggles the global `EmitHelper.ForceReflectionFallback` flag.
- netstandard2.0 needs `System.Reflection.Emit.ILGeneration` + `System.Reflection.Emit.Lightweight` packages; net462/net8.0+ have them in-box.
- `coverlet.collector` silently ignores `Threshold*` runsettings settings (thresholds only work in coverlet.msbuild); the 90% per-assembly gate is `CheckCoverageThreshold` in `build/Build.cs` union-merging every test project's cobertura report per shipped assembly. The union keys lines by (file, line) after stripping a leading `<assembly-name>/` path segment — coverlet writes referenced-project sources `src/`-relative in satellite reports but project-dir-relative in the owning project's report, and unnormalized keys double-count every cross-project line.
- Signing (ADR-0003): `FastReflection.snk` at the repo root signs every project via root `Directory.Build.props`; `AssemblyVersion` is package-major-only (computed from `$(Version)`), `FileVersion` is the full version. The core `InternalsVisibleTo` item carries `Key=` — regenerate that hex from the shipped dll's public key if the snk ever changes (it must not, post-publish). `sn -k`-equivalent keys need `CspParameters.KeyNumber=2` (AT_SIGNATURE) or the emitted public key won't match the blob.
- No `Microsoft.SourceLink.GitHub` package — SourceLink ships in the .NET 8+ SDK (package deprecated; 8.0.0 pulls GHSA-23fw-v26w-5fgq / NU1902). `PublishRepositoryUrl` + `RepositoryUrl` are enough.
- Per-TFM public API: shared `PublicAPI.*.txt` + `PublicAPI/<tfm>/` additions; net8.0 and net10.0 carry identical span-invoker additions (`NET8_0_OR_GREATER`). Package-validation suppressions live in `src/FastReflection/CompatibilitySuppressions.xml` incl. the CLR's automatic `_Attribute` COM interface on net462 (CP0008).
- `ci.yml` is Nuke-generated, never hand-edited; regenerate via `./build.cmd --generate-configuration GitHubActions_ci --host GitHubActions`. Nuke runs `./build.cmd` on every runner incl. ubuntu — `build.cmd` line 1 is a POSIX branch that `exec`s `build.sh`; both `build.cmd` and `build.sh` must stay `100755` in git.
- The `Test` target reads the test csproj `TargetFrameworks` and runs one `dotnet test -f <tfm>` per leg; `net<digits>` (.NET Framework) legs run only on Windows. New modern TFMs (e.g. net10.0) light up on both runners automatically.
- `benchmarks/baseline.json` IS the benchmark gate set: FastReflection library rows only (`MemberAccessBenchmarks` minus its BCL/direct rows — `DirectCall`, `PropertyInfo_GetValue`, `MethodInfo_Invoke`, `BclMethodInvoker_Invoke`, `TryParse_Direct`); `ReferenceLibraryBenchmarks` rows are reported, never gated, and a produced gated row missing from the file fails the gate too. Refresh is deliberate: `./build.cmd Benchmark --update-baseline`, review the diff, PR. `DotNext.Reflection` is net6.0+ only, so its comparison rows exist on net8.0 alone.
- The gate compares NORMALIZED means, not raw: each leg's `"$anchor"` entry is the geo-mean of that run's report-only rows, and a gated row fails when `mean/actualAnchor > baselineMean/baselineAnchor × 1.25`. Raw hosted-runner means swing 1.3-1.8x between consecutive windows-latest runs (whole-VM speed factor — each run is internally tight), so an absolute-mean gate cannot hold a 25% tolerance; normalization cancels the machine factor. Gated rows are excluded from the anchor so a suite-wide FastReflection regression is still caught; ~0ns rows like `DirectCall` fall under a 0.5ns floor.
- Baseline refresh from CI (calibrated to hosted runners, not a dev box): run the `benchmark` workflow (any branch), `gh run download <run-id> -n benchmarks`, place the report trees at `artifacts/benchmarks/<tfm>/`, then `./build.cmd Benchmark --update-baseline --benchmark-skip-run` writes `baseline.json` from those reports without re-measuring.
- The `benchmark` workflow is a separate Nuke `GitHubActions` attribute (windows-latest only; `Benchmark` is slow so it stays off the ci runners); regenerate `benchmark.yml` via `--generate-configuration GitHubActions_benchmark --host GitHubActions`. Its `PublishCondition = "!cancelled()"` uploads `artifacts/benchmarks` even when the gate fails — the reports are the regression evidence.

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues in Skymly/FastReflection. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical roles, labels equal to their names (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

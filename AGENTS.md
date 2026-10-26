# FastReflection — agent notes

## Commands

- Build: `dotnet build FastReflection.slnx` (slnx format; `global.json` pins the 10.0.x SDK band, `rollForward: latestFeature`)
- Test (all legs): `dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj`
- Test single leg: append `-f net8.0` or `-f net472` (net472 leg consumes the `net462` library build)
- Benchmarks: `dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -- --job short`
- Benchmark gate: `./build.cmd Benchmark` runs `--job short` per benchmark leg into `artifacts/benchmarks/<tfm>/` and fails when a gated row's mean is >25% slower than `benchmarks/baseline.json` (`--benchmark-tolerance` to override, `--benchmark-filter` for a glob subset)

## Structure

- `src/FastReflection/` — the library, multi-targeted `net462;netstandard2.0;net8.0`
  - `ReflectionExtensions.cs` — public API surface (CreateGetter/Setter/Invoker/Factory, Unreflect, net8 Span invokers)
  - `Emit/EmitHelper.cs` — DynamicMethod factory + IL micro-helpers + `DynamicCodeSupported` probe
  - `Emit/AccessorEmitter.cs` — IL emit for accesses and exact binds, plus reflection fallback
  - `TypeAccessor.cs`, `ObjectAccessor.cs`, `Member.cs` — type accessor, object accessor, and Member
  - `Polyfills.cs` — `RequiresUnreferencedCode`/`RequiresDynamicCode` for downlevel TFMs
- `tests/FastReflection.AotSmoke/` — PublishAot smoke app asserting the no-dynamic-code contract end-to-end; the Nuke `AotSmoke` target publishes linux-x64 and runs it, Linux only (invoked via `Test`). Exit 0 pass; nonzero names the failed scenario.

## Conventions / gotchas

- net462/netstandard2.0 have no `MethodInfo.CreateDelegate<T>()` — use `CreateDelegate(Type)` + cast.
- `RuntimeFeature` doesn't exist on net462/netstandard2.0 — dynamic-code probe is `#if`'d per TFM.
- Ref-returning property without setter is writable via getter + `stobj`; that write is an access, not `Member.CanWrite`. `ref readonly` is detected via `InAttribute`/`IsReadOnlyAttribute` modreq and has no write access.
- Struct member writes use `unbox` (managed pointer into the box) so writes persist — not `unbox.any`. See `docs/adr/0001-boxed-value-writes-update-the-box.md`.
- Tests disable xunit parallelization assembly-wide because `FallbackTests` toggles the global `EmitHelper.ForceReflectionFallback` flag.
- netstandard2.0 needs `System.Reflection.Emit.ILGeneration` + `System.Reflection.Emit.Lightweight` packages; net462/net8.0 have them in-box.
- `ci.yml` is Nuke-generated, never hand-edited; regenerate via `./build.cmd --generate-configuration GitHubActions_ci --host GitHubActions`. Nuke runs `./build.cmd` on every runner incl. ubuntu — `build.cmd` line 1 is a POSIX branch that `exec`s `build.sh`; both `build.cmd` and `build.sh` must stay `100755` in git.
- The `Test` target reads the test csproj `TargetFrameworks` and runs one `dotnet test -f <tfm>` per leg; `net<digits>` (.NET Framework) legs run only on Windows. New modern TFMs (e.g. net10.0) light up on both runners automatically.
- `benchmarks/baseline.json` IS the benchmark gate set: FastReflection library rows only (`MemberAccessBenchmarks` minus its BCL/direct rows — `DirectCall`, `PropertyInfo_GetValue`, `MethodInfo_Invoke`, `BclMethodInvoker_Invoke`, `TryParse_Direct`); `ReferenceLibraryBenchmarks` rows are reported, never gated, and a produced gated row missing from the file fails the gate too. Refresh is deliberate: `./build.cmd Benchmark --update-baseline`, review the diff, PR. `DotNext.Reflection` is net6.0+ only, so its comparison rows exist on net8.0 alone.
- Baseline must be calibrated to the hosted runner, not a dev box — windows-latest runs ~30-45% slower than typical local hardware, so dev-box numbers fail the gate in CI. To refresh from CI: run the `benchmark` workflow (any branch), `gh run download <run-id> -n benchmarks`, place the report trees at `artifacts/benchmarks/<tfm>/`, then `./build.cmd Benchmark --update-baseline --benchmark-skip-run` writes `baseline.json` from those reports without re-measuring.
- The `benchmark` workflow is a separate Nuke `GitHubActions` attribute (windows-latest only; `Benchmark` is slow so it stays off the ci runners); regenerate `benchmark.yml` via `--generate-configuration GitHubActions_benchmark --host GitHubActions`. Its `PublishCondition = "!cancelled()"` uploads `artifacts/benchmarks` even when the gate fails — the reports are the regression evidence.

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues in Skymly/FastReflection. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical roles, labels equal to their names (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

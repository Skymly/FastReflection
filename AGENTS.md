# FastReflection

High-performance .NET reflection library (`Skymly.FastReflection`) with satellite packages (`FastReflection.ObjectReader`, `FastReflection.XmlDocs`) targeting `net462;netstandard2.0;net8.0;net10.0`.

## Build & Verify

```powershell
dotnet build FastReflection.slnx
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj   # append -f net472|net6.0|net8.0|net10.0 for one leg
./build.cmd Test Coverage                                           # Nuke test + >=90% line-coverage gate
./build.cmd Pack                                                    # Pack src/* -> artifacts/ + strong-name check
./build.cmd Benchmark                                               # Benchmark regression gate (see docs/benchmarks.md)
```

- [`global.json`](global.json) pins the .NET 10.0 SDK band (`rollForward: latestFeature`).
- `net472` test leg runs on Windows only and consumes the `net462` library build (`net6.0` consumes `netstandard2.0`).
- `tests/FastReflection.AotSmoke/` validates the no-dynamic-code contract under `PublishAot` on `linux-x64` (`AotSmoke`, invoked via `Test` on Linux).

## Architecture & Invariants

- **Public-API-only satellites**: `src/FastReflection.ObjectReader/` and `src/FastReflection.XmlDocs/` consume `src/FastReflection/` through its public API only — never add inbound `InternalsVisibleTo` into core. Shipped TFMs and analyzer gates are hoisted in `src/Directory.Build.props`.
- **Strong naming (ADR-0003)**: [`FastReflection.snk`](FastReflection.snk) signs every project via [`Directory.Build.props`](Directory.Build.props). `AssemblyVersion` is package-major-only; `FileVersion` carries full version. Core `InternalsVisibleTo` carries `Key=` (regenerate with `CspParameters.KeyNumber=2` if `.snk` ever changes — never change post-publish).
- **Per-TFM Public API & compatibility**: Maintain shared `PublicAPI.*.txt` + `PublicAPI/<tfm>/` (`net8.0` and `net10.0` share span invokers). Package validation suppressions live in `src/FastReflection/CompatibilitySuppressions.xml`.

## Gotchas

- `net462` / `netstandard2.0` lack `MethodInfo.CreateDelegate<T>()` and `RuntimeFeature` — use `CreateDelegate(Type)` + cast and `#if`-guarded probes. `netstandard2.0` requires `System.Reflection.Emit.ILGeneration` + `Lightweight`.
- Ref-returning properties without setters are writable via getter + `stobj` (`Member.CanWrite` stays false); `ref readonly` (`InAttribute`/`IsReadOnlyAttribute` modreq) has no write access. Struct member writes use `unbox` (not `unbox.any`, [ADR-0001](docs/adr/0001-boxed-value-writes-update-the-box.md)).
- `FallbackTests` mutates global `EmitHelper.ForceReflectionFallback`, so assembly-wide xUnit parallelization is disabled.
- `coverlet.collector` ignores runsettings `Threshold*`; `CheckCoverageThreshold` in `build/Build.cs` union-merges cobertura reports after stripping leading `<assembly-name>/` prefixes to enforce the 90% per-assembly gate.
- Never add `Microsoft.SourceLink.GitHub` (deprecated with vulnerable transitive refs; .NET 8+ SDK `PublishRepositoryUrl` + `RepositoryUrl` suffice).
- `ci.yml` and `benchmark.yml` are Nuke-generated — never hand-edit; regenerate via `--generate-configuration GitHubActions_<name> --host GitHubActions`. Keep `build.cmd` and `build.sh` executable (`100755`) in git.
- Benchmark gate normalization & CI baseline refresh details live in [`docs/benchmarks.md`](docs/benchmarks.md).

## Docs & Agent Config

- Domain glossary: [`CONTEXT.md`](CONTEXT.md)
- ADRs & specs: [`docs/adr/`](docs/adr/), [`docs/specs/`](docs/specs/), [`docs/plans/1.0.md`](docs/plans/1.0.md), [`CHANGELOG.md`](CHANGELOG.md)
- Issue routing & triage labels: [`docs/agents/`](docs/agents/)

## Conventions

- Commit messages, issues, and PR text: **English**. Do not commit, push, tag, or publish unless explicitly requested.

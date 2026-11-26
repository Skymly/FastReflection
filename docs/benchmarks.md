# Benchmark Gate & Baseline Workflow

Details for the Nuke `Benchmark` target and `benchmarks/baseline.json` regression gate.

## Running Benchmarks

```powershell
# Ad-hoc BenchmarkDotNet run:
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -- --job short

# CI regression gate (per-TFM -> artifacts/benchmarks/<tfm>/):
./build.cmd Benchmark
```

Useful overrides: `--benchmark-tolerance`, `--benchmark-filter` (glob subset), and `--benchmark-skip-run` (evaluate or update against existing artifacts without re-measuring).

## Gated Rows & Anchor Normalization

- `benchmarks/baseline.json` defines the gated set: FastReflection library rows (`MemberAccessBenchmarks` minus BCL/direct rows: `DirectCall`, `PropertyInfo_GetValue`, `MethodInfo_Invoke`, `BclMethodInvoker_Invoke`, `TryParse_Direct`). Missing gated rows also fail the gate.
- `ReferenceLibraryBenchmarks` rows are reported for comparison and never gated. `DotNext.Reflection` targets `net6.0+`, so its comparison rows appear on `net8.0` only.
- **Normalization math**: Hosted `windows-latest` runs swing 1.3–1.8x between VMs, so raw absolute means cannot hold a 25% tolerance. Each leg's `"$anchor"` entry is the geometric mean of that run's report-only rows, and a gated row fails when:

  `mean / actualAnchor > baselineMean / baselineAnchor * 1.25`

- Gated rows are excluded from the anchor so a suite-wide FastReflection regression is still caught; near-zero rows such as `DirectCall` fall under a 0.5 ns floor.

## Refreshing `baseline.json` from CI

Calibrate baselines against hosted CI runners rather than local developer hardware:

1. Run the `benchmark` workflow on GitHub Actions (manual `workflow_dispatch`; kept off PR CI because it is slow). `PublishCondition = "!cancelled()"` uploads `artifacts/benchmarks` even when the gate fails.
2. Download the artifact (`gh run download <run-id> -n benchmarks`) and place the report trees under `artifacts/benchmarks/<tfm>/`.
3. Run `./build.cmd Benchmark --update-baseline --benchmark-skip-run`, review the `benchmarks/baseline.json` diff, and open a PR.

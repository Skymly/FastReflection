# Benchmarks

BenchmarkDotNet suite for `Skymly.FastReflection`, run under `--job short` on the `net472;net8.0;net10.0` legs. Two classes:

- `MemberAccessBenchmarks` — the FastReflection rows plus BCL/direct comparison rows (`DirectCall`, `PropertyInfo_GetValue`, `MethodInfo_Invoke`, `BclMethodInvoker_Invoke`, `TryParse_Direct`).
- `ReferenceLibraryBenchmarks` — the same operations measured against the libraries 1.0 was scoped against: fast-member accessor get/set and the `ObjectReader` feed, Dynamitey invoke/get/set/convert by name, Namotion `GetXmlDocsSummary`, DotNext.Reflection `Unreflect`/invoker. `DotNext.Reflection` has no .NET Framework asset, so its rows exist on net8.0+ legs only.

## Running

```bash
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -- --job short
```

The CI `benchmark` workflow (windows-latest) runs `./build.cmd Benchmark`, which executes `--job short` per leg into `artifacts/benchmarks/<tfm>/` and then applies the regression gate.

## The gate

`benchmarks/baseline.json` is the gate set: the FastReflection library rows of `MemberAccessBenchmarks` only. A gated row fails when its mean is more than **25% slower** than the baseline.

Raw means are never compared directly — hosted-runner whole-VM speed factors swing 1.3–1.8× between runs. Instead each leg's report-only rows (the BCL/direct rows) are geo-averaged into a per-run **anchor**, and the gate compares `mean/actualAnchor` against `baselineMean/baselineAnchor`. The machine factor cancels; a suite-wide FastReflection regression still fails because gated rows are excluded from the anchor. Rows under a 0.5 ns floor are excluded for stability.

`ReferenceLibraryBenchmarks` rows are **reported, never gated** — they exist so a reviewer can see the comparison, not to hold a budget.

## Refreshing the baseline

Deliberate maintainer action, never silent drift: `./build.cmd Benchmark --update-baseline`, review the diff, PR it. From CI instead of a dev box: run the `benchmark` workflow, `gh run download <run-id> -n benchmarks`, place the report trees at `artifacts/benchmarks/<tfm>/`, then `./build.cmd Benchmark --update-baseline --benchmark-skip-run` writes `baseline.json` from those reports without re-measuring. Useful overrides: `--benchmark-tolerance`, `--benchmark-filter`, `--benchmark-skip-run`.

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "ci",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    OnPushBranches = new[] { "main" },
    OnPullRequestBranches = new[] { "main" },
    InvokedTargets = new[] { nameof(Test) },
    EnableGitHubToken = true)]
// Dedicated workflow: benchmark legs are slow, so the gate keeps its own windows-latest
// job off the ci runners. cancelled()==false uploads the artifacts even when the gate
// fails — the run's reports are the regression evidence. (Written without a leading `!`:
// an unquoted `!expr` in YAML parses as a tag and the workflow file fails to load.)
[GitHubActions(
    "benchmark",
    GitHubActionsImage.WindowsLatest,
    OnPushBranches = new[] { "main" },
    OnPullRequestBranches = new[] { "main" },
    InvokedTargets = new[] { nameof(Benchmark) },
    PublishCondition = "cancelled() == false")]
[GitHubActions(
    "publish",
    GitHubActionsImage.WindowsLatest,
    OnPushTags = new[] { "v*.*.*" },
    InvokedTargets = new[] { nameof(Publish) },
    ImportSecrets = new[] { nameof(NuGetApiKey) },
    EnableGitHubToken = true)]
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Test);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("NuGet API key")]
    readonly string NuGetApiKey = null!;

    // The 1.0 benchmark gate (docs/specs/1.0-quality-gates.md); the tolerance covers
    // hosted-runner jitter — a maintainer override tunes it without reopening the spec.
    [Parameter("Benchmark regression tolerance vs baseline, as a fraction - default 0.25")]
    readonly double BenchmarkTolerance = 0.25;

    // Baseline refresh is a deliberate maintainer run plus PR — never silent drift.
    [Parameter("Rewrite benchmarks/baseline.json from this run's results instead of gating")]
    readonly bool UpdateBaseline;

    [Parameter("BenchmarkDotNet filter glob - default '*' runs the whole suite")]
    readonly string BenchmarkFilter = "*";

    AbsolutePath SolutionFile => RootDirectory / "FastReflection.slnx";
    AbsolutePath LibraryProject => RootDirectory / "src" / "FastReflection" / "FastReflection.csproj";
    AbsolutePath TestProject => RootDirectory / "tests" / "FastReflection.Tests" / "FastReflection.Tests.csproj";
    AbsolutePath SmokeProject => RootDirectory / "tests" / "FastReflection.AotSmoke" / "FastReflection.AotSmoke.csproj";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath BenchmarkProject => RootDirectory / "benchmarks" / "FastReflection.Benchmarks" / "FastReflection.Benchmarks.csproj";
    AbsolutePath BenchmarkDirectory => ArtifactsDirectory / "benchmarks";
    AbsolutePath BenchmarkBaselineFile => RootDirectory / "benchmarks" / "baseline.json";

    Target Clean => _ => _
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s.SetProjectFile(SolutionFile));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(SolutionFile)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    // .NET Framework test legs (net472, net48, ...) only run on Windows; modern legs (net6.0+) run everywhere
    static bool IsNetFrameworkTfm(string tfm) => tfm.StartsWith("net") && tfm[3..].All(char.IsDigit);

    static IEnumerable<string> TargetFrameworksOf(AbsolutePath project)
        => XDocument.Load(project)
            .Descendants("TargetFrameworks").Single().Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // linux-x64 PublishAot smoke — the only RID at 1.0 — runs on an x64 Linux host only.
    Target AotSmoke => _ => _
        .DependsOn(Compile)
        .OnlyWhenStatic(() => OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
        .Executes(() =>
        {
            foreach (var framework in TargetFrameworksOf(SmokeProject))
            {
                var publishDir = ArtifactsDirectory / "aotsmoke" / framework;
                DotNetPublish(s => s
                    .SetProject(SmokeProject)
                    .SetConfiguration(Configuration)
                    .SetFramework(framework)
                    .SetRuntime("linux-x64")
                    .SetOutput(publishDir));

                ProcessTasks.StartProcess(publishDir / SmokeProject.NameWithoutExtension, workingDirectory: publishDir)
                    .AssertZeroExitCode();
            }
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .DependsOn(AotSmoke)
        .Executes(() =>
        {
            var frameworks = TargetFrameworksOf(TestProject)
                .Where(tfm => OperatingSystem.IsWindows() || !IsNetFrameworkTfm(tfm));

            DotNetTest(s => s
                .SetProjectFile(TestProject)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .CombineWith(frameworks, (settings, framework) => settings
                    .SetFramework(framework)));
        });

    // Benchmark gate (docs/specs/1.0-quality-gates.md): every leg runs the suite under
    // --job short, then each entry in the committed baseline.json is compared against the
    // produced mean — a FastReflection row >25% slower fails the job. BCL-baseline rows and
    // reference-library rows are reported only. net10.0 joins automatically once the
    // benchmark project gains the TFM.
    Target Benchmark => _ => _
        .Produces(BenchmarkDirectory)
        .Executes(() =>
        {
            BenchmarkDirectory.CreateOrCleanDirectory();

            var frameworks = TargetFrameworksOf(BenchmarkProject)
                .Where(tfm => OperatingSystem.IsWindows() || !IsNetFrameworkTfm(tfm))
                .ToList();

            foreach (var framework in frameworks)
            {
                // Release unconditionally — numbers under the Debug default are meaningless.
                ProcessTasks.StartProcess("dotnet",
                        $"run --project \"{BenchmarkProject}\" --configuration Release --framework {framework} -- " +
                        $"--job short --filter \"{BenchmarkFilter}\" --artifacts \"{BenchmarkDirectory / framework}\" --exporters json",
                        workingDirectory: RootDirectory)
                    .AssertZeroExitCode();
            }

            if (UpdateBaseline && BenchmarkFilter != "*")
                Serilog.Log.Warning("--update-baseline with --benchmark-filter '{Filter}' writes a partial baseline", BenchmarkFilter);

            if (UpdateBaseline)
                WriteBenchmarkBaseline(frameworks);
            else
                CheckBenchmarkGate(frameworks);
        });

    // benchmarks/baseline.json is shaped { "<tfm>": { "<Type>.<Method>": <mean ns> } } and
    // IS the gate set: FastReflection library rows only. The BCL/direct comparisons in
    // MemberAccessBenchmarks and every ReferenceLibraryBenchmarks row are reported-only —
    // the default-deny predicate puts any other produced row behind the gate, so a new
    // library row cannot merge unbaselined.
    static readonly string[] ReportedOnlyBenchmarks =
    {
        "DirectCall", "PropertyInfo_GetValue", "MethodInfo_Invoke", "BclMethodInvoker_Invoke", "TryParse_Direct",
    };

    static bool IsGatedBenchmark(string type, string method) =>
        type != "ReferenceLibraryBenchmarks" &&
        !(type == "MemberAccessBenchmarks" && ReportedOnlyBenchmarks.Contains(method));

    // Reads <artifacts>/benchmarks/<tfm>/results/*-report-*.json → one row per benchmark.
    static List<(string Name, string Type, string Method, double Mean)> ReadBenchmarkRows(AbsolutePath legDirectory)
    {
        var rows = new List<(string Name, string Type, string Method, double Mean)>();
        foreach (var report in legDirectory.GlobFiles("results/*-report-*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(report));
            foreach (var benchmark in doc.RootElement.GetProperty("Benchmarks").EnumerateArray())
            {
                var name = benchmark.GetProperty("FullName").GetString()!;
                Assert.True(rows.All(x => x.Name != name), $"Duplicate benchmark result for {name} in {report}");
                rows.Add((name,
                    benchmark.GetProperty("Type").GetString()!,
                    benchmark.GetProperty("Method").GetString()!,
                    benchmark.GetProperty("Statistics").GetProperty("Mean").GetDouble()));
            }
        }

        Assert.True(rows.Count > 0, $"No BenchmarkDotNet reports found under {legDirectory}");
        return rows;
    }

    Dictionary<string, Dictionary<string, double>> LoadBenchmarkBaseline()
    {
        Assert.FileExists(BenchmarkBaselineFile,
            "Benchmark baseline missing — produce it deliberately: ./build.cmd Benchmark --update-baseline, review, PR");

        var baseline = new Dictionary<string, Dictionary<string, double>>();
        using var doc = JsonDocument.Parse(File.ReadAllText(BenchmarkBaselineFile));
        foreach (var leg in doc.RootElement.EnumerateObject())
            baseline[leg.Name] = leg.Value.EnumerateObject()
                .ToDictionary(x => x.Name, x => x.Value.GetDouble());
        return baseline;
    }

    void CheckBenchmarkGate(IReadOnlyCollection<string> frameworks)
    {
        var baseline = LoadBenchmarkBaseline();
        var failures = new List<string>();

        foreach (var framework in frameworks)
        {
            var results = ReadBenchmarkRows(BenchmarkDirectory / framework);

            // A leg with no baseline section is a new TFM mid-flight: reported only until
            // the next deliberate refresh writes it.
            if (!baseline.TryGetValue(framework, out var baselineRows))
            {
                Serilog.Log.Warning(
                    "Benchmark leg {Framework} has no baseline section — its rows are reported only; " +
                    "refresh benchmarks/baseline.json to put them behind the gate",
                    framework);
                baselineRows = new Dictionary<string, double>();
            }

            foreach (var row in results.OrderBy(x => x.Name))
            {
                if (!baselineRows.TryGetValue(row.Name, out var baselineMean))
                {
                    if (IsGatedBenchmark(row.Type, row.Method))
                        failures.Add($"{framework} {row.Name}: gated row has no baseline entry — refresh benchmarks/baseline.json");
                    else
                        Serilog.Log.Information("{Framework} {Name}: {Mean:F1} ns (reported only)", framework, row.Name, row.Mean);
                    continue;
                }

                var limit = baselineMean * (1 + BenchmarkTolerance);
                if (row.Mean > limit)
                    failures.Add($"{framework} {row.Name}: {row.Mean:F1} ns is more than {BenchmarkTolerance:P0} slower than baseline {baselineMean:F1} ns");
                else
                    Serilog.Log.Information("{Framework} {Name}: {Mean:F1} ns vs baseline {Baseline:F1} ns", framework, row.Name, row.Mean, baselineMean);
            }

            foreach (var name in baselineRows.Keys.Except(results.Select(x => x.Name)))
                failures.Add($"{framework} {name}: baseline entry produced no result");
        }

        Assert.True(failures.Count == 0, "Benchmark gate failed:\n  " + string.Join("\n  ", failures));
    }

    void WriteBenchmarkBaseline(IReadOnlyCollection<string> frameworks)
    {
        var baseline = new SortedDictionary<string, SortedDictionary<string, double>>(StringComparer.Ordinal);
        foreach (var framework in frameworks)
        {
            baseline[framework] = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (var row in ReadBenchmarkRows(BenchmarkDirectory / framework))
            {
                if (IsGatedBenchmark(row.Type, row.Method))
                    baseline[framework][row.Name] = row.Mean;
            }
        }

        File.WriteAllText(BenchmarkBaselineFile,
            JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Serilog.Log.Warning(
            "Wrote {Path} — review the diff before committing; baseline refresh is a deliberate maintainer act",
            BenchmarkBaselineFile);
    }

    Target Pack => _ => _
        .DependsOn(Test)
        .Produces(ArtifactsDirectory / "*.nupkg")
        .Executes(() =>
        {
            DotNetPack(s => s
                .SetProject(LibraryProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(ArtifactsDirectory)
                .EnableNoBuild());
        });

    Target Publish => _ => _
        .DependsOn(Pack)
        .Requires(() => NuGetApiKey)
        .Executes(() =>
        {
            DotNetNuGetPush(s => s
                .SetSource("https://api.nuget.org/v3/index.json")
                .SetApiKey(NuGetApiKey)
                .EnableSkipDuplicate()
                .CombineWith(ArtifactsDirectory.GlobFiles("*.nupkg"), (settings, path) => settings
                    .SetTargetPath(path)));
        });
}

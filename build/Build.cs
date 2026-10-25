using System.Globalization;
using System.Runtime.InteropServices;
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
    InvokedTargets = new[] { nameof(Test), nameof(Coverage) },
    EnableGitHubToken = true,
    // Coverage runs on both runners but only one artifact may be uploaded; windows-latest is
    // the primary (full-matrix) leg. !cancelled() keeps the report on gate-failure runs.
    PublishCondition = "runner.os == 'Windows' && !cancelled()")]
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

    // The 1.0 quality gate (docs/specs/1.0-quality-gates.md); a maintainer override tunes the
    // number without reopening the spec.
    [Parameter("Minimum line coverage per shipped assembly, as a fraction - default 0.90")]
    readonly double CoverageThreshold = 0.90;

    AbsolutePath SolutionFile => RootDirectory / "FastReflection.slnx";
    AbsolutePath LibraryProject => RootDirectory / "src" / "FastReflection" / "FastReflection.csproj";
    AbsolutePath TestProject => RootDirectory / "tests" / "FastReflection.Tests" / "FastReflection.Tests.csproj";
    AbsolutePath SmokeProject => RootDirectory / "tests" / "FastReflection.AotSmoke" / "FastReflection.AotSmoke.csproj";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath CoverageRunSettings => TestProject.Parent / "coverage.runsettings";
    AbsolutePath CoverageDirectory => ArtifactsDirectory / "coverage";

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

    // Modern legs look like net6.0/net8.0/net10.0: "net" + a version with a dot (netstandard2.0
    // and netcoreapp3.1 don't qualify, and neither is a test leg anyway)
    static bool IsModernTfm(string tfm) =>
        tfm.StartsWith("net") && char.IsDigit(tfm.ElementAtOrDefault(3)) && tfm.Contains('.');

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

    // Coverage (docs/specs/1.0-quality-gates.md): coverlet.collector produces cobertura on the
    // newest modern leg only — net8.0 today, net10.0 automatically once it joins the test
    // matrix. coverlet.collector ignores its Threshold settings, so the per-assembly line gate
    // is enforced by CheckCoverageThreshold on the produced report.
    Target Test => _ => _
        .DependsOn(Compile)
        .DependsOn(AotSmoke)
        .Executes(() =>
        {
            // Clean first so a stale report can never satisfy the gate
            CoverageDirectory.CreateOrCleanDirectory();

            var frameworks = TargetFrameworksOf(TestProject)
                .Where(tfm => OperatingSystem.IsWindows() || !IsNetFrameworkTfm(tfm))
                .ToList();
            var coverageFramework = frameworks
                .Where(IsModernTfm)
                .MaxBy(tfm => Version.Parse(tfm[3..]));
            Assert.NotNull(coverageFramework, "No modern (non-.NET-Framework) test leg found for coverage collection");

            DotNetTest(s => s
                .SetProjectFile(TestProject)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .CombineWith(frameworks, (settings, framework) =>
                {
                    settings = settings.SetFramework(framework);
                    if (framework == coverageFramework)
                    {
                        settings = settings
                            .SetDataCollector("XPlat Code Coverage")
                            .SetSettingsFile(CoverageRunSettings)
                            .SetResultsDirectory(CoverageDirectory);
                    }

                    return settings;
                }));

            CheckCoverageThreshold();
        });

    // Cobertura emits one <package> per shipped assembly; the spec gates each of them.
    void CheckCoverageThreshold()
    {
        var report = CoverageDirectory.GlobFiles("**/coverage.cobertura.xml")
            .MaxBy(x => File.GetLastWriteTimeUtc(x));
        Assert.NotNull(report, $"No cobertura report found under {CoverageDirectory}");

        var modules = XDocument.Load(report).Descendants("package").ToList();
        Assert.True(modules.Count > 0, $"Coverage gate: no shipped assemblies found in {report}");

        foreach (var module in modules)
        {
            var name = module.Attribute("name")!.Value;
            var lineRate = double.Parse(module.Attribute("line-rate")!.Value, CultureInfo.InvariantCulture);
            Assert.True(lineRate >= CoverageThreshold,
                $"Coverage gate: {name} line coverage {lineRate:P1} is below {CoverageThreshold:P0}");
        }
    }

    // Artifact marker for the generated workflows: only the ci workflow invokes this target, so
    // the coverage upload step never reaches publish.yml (the spec keeps publish.yml untouched).
    Target Coverage => _ => _
        .DependsOn(Test)
        .Produces(CoverageDirectory);

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

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

    AbsolutePath SolutionFile => RootDirectory / "FastReflection.slnx";
    AbsolutePath LibraryProject => RootDirectory / "src" / "FastReflection" / "FastReflection.csproj";
    AbsolutePath TestProject => RootDirectory / "tests" / "FastReflection.Tests" / "FastReflection.Tests.csproj";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

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

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var frameworks = XDocument.Load(TestProject)
                .Descendants("TargetFrameworks").Single().Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(tfm => OperatingSystem.IsWindows() || !IsNetFrameworkTfm(tfm));

            DotNetTest(s => s
                .SetProjectFile(TestProject)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .CombineWith(frameworks, (settings, framework) => settings
                    .SetFramework(framework)));
        });

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

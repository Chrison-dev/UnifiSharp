using System;
using System.IO;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Serilog;

// UnifiSharp build pipeline — Fallout build (NUKE successor), per ADR-0001.
//
// Wraps the native `dotnet` toolchain (build regenerates UnifiSharp.Api from the
// committed OpenAPI spec via Kiota, then compiles). Targets:
//   Compile  → dotnet build the solution (-c Release)
//   Test     → dotnet test (--no-build)
//   Pack     → dotnet pack the packable projects into artifacts/ (optional --version-suffix)
//   Publish  → dotnet nuget push artifacts/*.nupkg to nuget.org (Trusted Publishing key)
//
//   ./build.sh                 # default: Test
//   ./build.sh Pack --version-suffix preview.42
//   ./build.sh Publish --nuget-api-key <key>   # key minted by NuGet/login OIDC in CI
class Build : FalloutBuild
{
    public static int Main() => Execute<Build>(x => x.Test);

    const string Configuration = "Release";

    [Parameter("Prerelease suffix appended to VersionPrefix (e.g. 'preview.42'). Empty → stable.")]
    readonly string VersionSuffix;

    [Parameter("nuget.org push source.")]
    readonly string NuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("API key for the push. In CI this is the short-lived key from NuGet/login (Trusted Publishing), never a stored secret.")]
    readonly string NuGetApiKey;

    AbsolutePath Solution => RootDirectory / "UnifiSharp.slnx";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    // Build the command into a plain string first: passing an interpolated string
    // *directly* to StartProcess binds Fallout's ArgumentStringHandler, which
    // auto-quotes each interpolation hole — collapsing a multi-token argument list
    // into one quoted arg. A pre-built string takes the plain overload.
    void Dotnet(string arguments)
    {
        string command = arguments;
        ProcessTasks.StartProcess("dotnet", command, workingDirectory: RootDirectory).AssertZeroExitCode();
    }

    Target Clean => _ => _
        .Executes(() =>
        {
            if (Directory.Exists(ArtifactsDirectory))
                Directory.Delete(ArtifactsDirectory, recursive: true);
        });

    // `dotnet build` triggers UnifiSharp.Api's GenerateUnifiClient target, which runs
    // `dotnet tool restore` (Kiota, pinned in .config/dotnet-tools.json) and regenerates
    // Generated/ (gitignored) from the committed OpenAPI spec before compiling.
    Target Compile => _ => _
        .Executes(() => Dotnet($"build {Solution} -c {Configuration} --nologo"));

    // Live integration tests skip automatically without a secrets.env.
    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() => Dotnet($"test {Solution} -c {Configuration} --no-build --nologo"));

    // Packs the packable projects (UnifiSharp, .Api, .Cli — Tests are IsPackable=false).
    // Packs fresh (no --no-build) so a -preview.N suffix stamps the assemblies too.
    // Package IDs carry the Chrison.* prefix (see the csprojs).
    Target Pack => _ => _
        .DependsOn(Clean, Test)
        .Executes(() =>
        {
            string suffix = string.IsNullOrWhiteSpace(VersionSuffix) ? "" : $" --version-suffix {VersionSuffix}";
            Dotnet($"pack {Solution} -c {Configuration} -o {ArtifactsDirectory}{suffix}");
        });

    // Pushes every .nupkg in artifacts/ to nuget.org. --skip-duplicate makes re-runs
    // idempotent; --no-symbols because symbols are embedded (DebugType=embedded).
    Target Publish => _ => _
        .DependsOn(Pack)
        .Executes(() =>
        {
            if (string.IsNullOrWhiteSpace(NuGetApiKey))
                throw new Exception("Missing --nuget-api-key. In CI this is minted by NuGet/login (Trusted Publishing); locally, pass a temporary key.");
            Log.Information("Pushing packages from {Artifacts} to {Source}", ArtifactsDirectory, NuGetSource);
            AbsolutePath glob = ArtifactsDirectory / "*.nupkg";
            Dotnet($"nuget push {glob} --source {NuGetSource} --api-key {NuGetApiKey} --skip-duplicate --no-symbols");
        });
}

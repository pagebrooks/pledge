using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;

namespace PledgeContracts.Build.Tests;

// Locates the SDK and the source tree and creates fixture projects in a fresh temporary folder each. A fixture
// consumes Pledge the way a package reference would: assemblies as references, the generator as an analyzer and
// Pledge.targets imported after the project body. The assemblies are copied into the fixture so the compiler
// server, which caches analyzers by path, always loads the ones this test run was built with.
internal static class BuildHarness
{
    private static readonly Lazy<string> _repositoryRoot = new(FindRepositoryRoot);

    public static string RepositoryRoot => _repositoryRoot.Value;

    public static string TargetsPath => Path.Combine(RepositoryRoot, "src", "Pledge", "build", "Pledge.targets");

    // The host that runs this test process belongs to the SDK the repository pins; the fixtures use the same one.
    public static string Dotnet { get; } = FindDotnet();

    public static string FixtureSource(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "Fixtures", .. parts]);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pledge.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No Pledge.slnx above {AppContext.BaseDirectory}.");
    }

    private static string FindDotnet()
    {
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath && File.Exists(hostPath))
        {
            return hostPath;
        }

        // <root>/shared/Microsoft.NETCore.App/<version>/ is where this runtime lives; the host sits at <root>.
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var candidate = Path.Combine(root, executable);
        return File.Exists(candidate) ? candidate : executable;
    }
}

internal sealed class Fixture : IDisposable
{
    private static readonly string[] _pledgeAssemblies = ["Pledge.dll", "Pledge.Attributes.dll", "Pledge.Roslyn.dll"];

    private Fixture(string directory) => Directory = directory;

    public string Directory { get; }

    public static Fixture Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pledge-build-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(BuildHarness.RepositoryRoot, "global.json"), Path.Combine(directory, "global.json"));

        var lib = Path.Combine(directory, "lib");
        System.IO.Directory.CreateDirectory(lib);
        foreach (var assembly in _pledgeAssemblies)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, assembly), Path.Combine(lib, assembly));
        }

        return new Fixture(directory);
    }

    // Writes <name>/<name>.csproj. Properties go in the project body, where a consumer would put them; withPledge
    // false gives the same project without the package, for the Ignore comparison.
    public string WriteProject(string name, bool withPledge = true, string outputType = "Library", IReadOnlyDictionary<string, string>? properties = null)
    {
        var projectDirectory = Path.Combine(Directory, name);
        System.IO.Directory.CreateDirectory(projectDirectory);

        var builder = new StringBuilder();
        builder.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        builder.AppendLine("  <PropertyGroup>");
        builder.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    <OutputType>{outputType}</OutputType>");
        builder.AppendLine("    <Nullable>enable</Nullable>");
        builder.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        builder.AppendLine("    <AssemblyName>Fixture</AssemblyName>");
        builder.AppendLine("    <RootNamespace>Fixture</RootNamespace>");
        // The same path in every fixture's PDB references, so projects in sibling folders differ only in content.
        builder.AppendLine("    <PathMap>$(MSBuildProjectDirectory)=/_/</PathMap>");
        foreach (var (key, value) in properties ?? new Dictionary<string, string>())
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"    <{key}>{SecurityElement.Escape(value)}</{key}>");
        }

        builder.AppendLine("  </PropertyGroup>");
        if (withPledge)
        {
            builder.AppendLine("  <ItemGroup>");
            builder.AppendLine("    <Reference Include=\"../lib/Pledge.dll\" />");
            builder.AppendLine("    <Reference Include=\"../lib/Pledge.Attributes.dll\" />");
            builder.AppendLine("    <Analyzer Include=\"../lib/Pledge.Roslyn.dll\" />");
            builder.AppendLine("  </ItemGroup>");
            builder.AppendLine(CultureInfo.InvariantCulture, $"  <Import Project=\"{SecurityElement.Escape(BuildHarness.TargetsPath)}\" />");
        }

        builder.AppendLine("</Project>");

        var path = Path.Combine(projectDirectory, name + ".csproj");
        File.WriteAllText(path, builder.ToString());
        return path;
    }

    public void WriteSource(string project, string fileName, string content) =>
        File.WriteAllText(Path.Combine(Directory, project, fileName), content);

    public void CopySources(string project, params string[] fixtureFolder)
    {
        foreach (var file in System.IO.Directory.GetFiles(BuildHarness.FixtureSource(fixtureFolder), "*.cs"))
        {
            File.Copy(file, Path.Combine(Directory, project, Path.GetFileName(file)));
        }
    }

    // dotnet build with node reuse off, so no MSBuild worker outlives the test holding files in the fixture.
    public Task<ProcessResult> BuildAsync(string project, string configuration, params string[] arguments) =>
        DotnetAsync(["build", Path.Combine(Directory, project), "--configuration", configuration, "-nodeReuse:false", "-nologo", .. arguments]);

    public string OutputPath(string project, string configuration, string fileName = "Fixture.dll") =>
        Path.Combine(Directory, project, "bin", configuration, "net10.0", fileName);

    // Evaluates the project and returns the final values of the given properties, as MSBuild sees them after
    // every import, without restoring or building anything.
    public async Task<IReadOnlyDictionary<string, string>> EvaluateAsync(string project, IEnumerable<string> properties, params string[] arguments)
    {
        var names = properties.ToList();
        var result = await DotnetAsync(["msbuild", Path.Combine(Directory, project), "-nologo", .. names.Select(name => "-getProperty:" + name), .. arguments]);
        result.AssertSucceeded();

        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement.GetProperty("Properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    public Task<ProcessResult> RunTargetAsync(string project, string target, params string[] arguments) =>
        DotnetAsync(["msbuild", Path.Combine(Directory, project), "-nologo", "-nodeReuse:false", "-t:" + target, .. arguments]);

    public Task<ProcessResult> DotnetAsync(params string[] arguments) => ProcessRunner.RunAsync(BuildHarness.Dotnet, Directory, arguments);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A compiler server can hold a file briefly after the build; the folder is in the temp directory anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public string Combined => Output + Error;

    public void AssertSucceeded() => Assert.True(ExitCode == 0, $"Exit code {ExitCode}.{Environment.NewLine}{Combined}");
}

internal static class ProcessRunner
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);

    public static async Task<ProcessResult> RunAsync(string fileName, string workingDirectory, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The test host runs inside dotnet test, which exports MSBuild settings meant for its own build; a child
        // build that inherits them can pick up the wrong SDK or MSBuild instance.
        foreach (var key in startInfo.Environment.Keys.ToList())
        {
            if (key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase) || key.StartsWith("_MSBuild", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.Environment.Remove(key);
            }
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(_timeout);

        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(process.ExitCode, await output, await error);
    }
}

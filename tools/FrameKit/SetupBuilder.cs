using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FrameKit;

public static partial class SetupBuilder
{
    public static readonly string[] DefaultPlatforms = ["win-x64", "linux-x64"];
    private static readonly TimeSpan SelfCheckTimeout = TimeSpan.FromMinutes(2);

    public sealed record Settings(string Manifest, string Package, string Out, IReadOnlyList<string> Platforms);

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NotAlphanumeric();

    [GeneratedRegex("^(win|linux|osx)-(x64|arm64)$")]
    private static partial Regex PlatformPattern();

    public static void Build(Settings settings)
    {
        var manifest = FrameAppManifest.Load(settings.Manifest, payload: null);
        var package = Path.GetFullPath(settings.Package);
        if (!File.Exists(package))
        {
            throw new KitError($"package not found: {package}");
        }
        if (Path.GetFileName(package) != manifest.PackageFileName)
        {
            throw new KitError($"package {Path.GetFileName(package)} does not match {manifest.Id} {manifest.Version} from {manifest.FilePath}");
        }
        foreach (var platform in settings.Platforms)
        {
            if (!PlatformPattern().IsMatch(platform))
            {
                throw new KitError($"unknown platform '{platform}' (use win-x64, linux-x64, linux-arm64, ...)");
            }
        }
        var outDir = Path.GetFullPath(settings.Out);
        Directory.CreateDirectory(outDir);
        var assembly = $"{FileStem(manifest)}-Setup-{manifest.Version}";
        var work = Directory.CreateTempSubdirectory("framekit-setup-").FullName;
        try
        {
            var branding = Path.Combine(work, "app-setup.json");
            File.WriteAllText(branding, new JsonObject { ["found_it"] = manifest.SetupFoundIt }.ToJsonString());
            var icon = manifest.SetupIcon.Length > 0 ? manifest.SetupIconPath : null;
            foreach (var platform in settings.Platforms)
            {
                BuildOne(manifest, package, branding, icon, assembly, platform, work, outDir);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public static string FileStem(FrameAppManifest manifest)
    {
        var letters = NotAlphanumeric().Replace(manifest.Name, "");
        return letters.Any(char.IsAsciiLetter) ? letters : manifest.Id;
    }

    public static List<string> PublishArguments(string project, string platform, string publishDir, string artifacts,
                                                string assembly, string package, string branding, string? icon,
                                                string product, string company)
    {
        var arguments = new List<string>
        {
            "publish", project, "-c", "Release", "-r", platform, "--self-contained", "true",
            $"-p:PublishDir={MsBuildEscape(Path.TrimEndingDirectorySeparator(publishDir) + Path.DirectorySeparatorChar)}",
            $"-p:ArtifactsPath={MsBuildEscape(artifacts)}",
            "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
            "-p:DebugType=none", $"-p:SetupAssemblyName={MsBuildEscape(assembly)}", $"-p:AppPackage={MsBuildEscape(package)}",
            $"-p:AppBranding={MsBuildEscape(branding)}", $"-p:Product={MsBuildEscape(product)}", $"-p:Company={MsBuildEscape(company)}",
            "-nologo", "-v:quiet", "-tl:off",
        };
        if (icon is not null)
        {
            arguments.Add($"-p:AppIcon={MsBuildEscape(icon)}");
        }
        return arguments;
    }

    public static string MsBuildEscape(string value)
    {
        var escaped = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            escaped.Append(c is '%' or ';' or ',' or '$' or '@' or '\'' or '"' or '*' or '?' ? $"%{(int)c:X2}" : c.ToString());
        }
        return escaped.ToString();
    }

    private static void BuildOne(FrameAppManifest manifest, string package, string branding, string? icon, string assembly,
                                 string platform, string work, string outDir)
    {
        Console.WriteLine($"Building {assembly} for {platform}");
        var publishDir = Path.Combine(work, platform);
        var arguments = PublishArguments(KitPaths.SetupProject, platform, publishDir, Path.Combine(work, "build"), assembly,
                                         package, branding, icon, manifest.Name + " Setup", manifest.Publisher);
        var (exitCode, output) = Run("dotnet", arguments, TimeSpan.FromMinutes(20));
        var isWindows = platform.StartsWith("win-", StringComparison.Ordinal);
        var built = Path.Combine(publishDir, isWindows ? assembly + ".exe" : assembly);
        if (exitCode != 0 || !File.Exists(built))
        {
            foreach (var line in output.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(output.LastOrDefault() ?? ""))
            {
                Console.Error.WriteLine("  " + line.Trim());
            }
            throw new KitError($"{platform} build failed");
        }

        string target;
        if (isWindows)
        {
            target = Path.Combine(outDir, assembly + ".exe");
            File.Copy(built, target, overwrite: true);
        }
        else
        {
            target = Path.Combine(outDir, $"{assembly}-{platform}.tar.gz");
            File.WriteAllBytes(target, PackageArchive.SingleFileTarGz(assembly, File.ReadAllBytes(built), PackageArchive.Executable,
                                                                      DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        }

        if (platform == HostPlatform())
        {
            SelfCheck(built, manifest, work);
        }
        else
        {
            Console.WriteLine("  self-check: skipped (wrong platform)");
        }
        Console.WriteLine($"  {target} ({new FileInfo(target).Length / 1048576.0:F1} MB)");
    }

    private static void SelfCheck(string program, FrameAppManifest manifest, string work)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(program, PackageArchive.Executable);
        }
        var report = Path.Combine(work, "self-check.txt");
        var (exitCode, _) = Run(program, ["--self-check", report], SelfCheckTimeout);
        var text = File.Exists(report) ? File.ReadAllText(report) : "no report";
        if (exitCode != 0 || !text.StartsWith($"ok {manifest.Id} {manifest.Version}", StringComparison.Ordinal))
        {
            throw new KitError($"self-check failed: {text.Trim()}");
        }
        var lines = text.Split('\n');
        var icon = lines.FirstOrDefault(l => l.StartsWith("icon ", StringComparison.Ordinal))?.Trim();
        Console.WriteLine($"  self-check: {lines[0].Trim()}{(icon is null ? "" : $" ({icon})")}");
    }

    private static string HostPlatform()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return $"{os}-{arch}";
    }

    private static (int ExitCode, List<string> Output) Run(string program, IEnumerable<string> arguments, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["AVALONIA_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        var output = new List<string>();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.Add(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.Add(e.Data); } } };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new KitError($"can not start {program}: {exception.Message}");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new KitError($"{Path.GetFileName(program)} did not respond in time");
        }
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}

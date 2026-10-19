using System.Security.Cryptography;
using System.Text;

namespace FrameKit;

public static class Packager
{
    private static readonly string[] KitScripts =
        [FrameKitConstants.InstallScript, FrameKitConstants.UninstallScript, FrameKitConstants.UpdateScript, FrameKitConstants.ConstantsScript];
    private static readonly string[] PanelFiles =
        [.. PanelSignature.SignedFiles, FrameKitConstants.PanelSums, FrameKitConstants.PanelSignature];
    private static readonly HashSet<string> TextSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sh", ".service", ".timer", ".json", ".svg", ".txt", ".js", ".conf", ".env", ".pem", ".vrdrivermanifest", ".cfg", ".ini", ".md",
        ".py", ".lua", ".pl", ".rb", ".desktop", ".yaml", ".yml", ".toml", ".xml", ".css", ".html",
    };

    public sealed record Settings(string Manifest, string Payload, string Out, string Key, string? Panel, long Epoch, bool AllowFileUrls,
        string? PanelKey = null, string? NextKey = null);

    public static string Build(Settings settings)
    {
        using var key = ReleaseKey.Load(settings.Key);
        var payload = Path.GetFullPath(settings.Payload);
        if (!Directory.Exists(payload))
        {
            throw new KitError($"payload directory not found: {payload}");
        }
        var manifest = FrameAppManifest.Load(settings.Manifest, payload, settings.AllowFileUrls);
        var panelKey = settings.PanelKey ?? KitPaths.PanelPublicKey;
        var (panelVersion, panelFiles) = ReadPanel(settings.Panel, panelKey);
        var signingKey = key.PublicKeyPem;
        var (trustedKey, trustedFingerprint) = settings.NextKey is null ? (signingKey, key.Fingerprint) : ReleaseKey.LoadPublic(settings.NextKey);
        if (settings.NextKey is not null && trustedFingerprint == key.Fingerprint)
        {
            throw new KitError($"--next-key {settings.NextKey} cannot be the same key");
        }

        using var archive = new PackageArchive(settings.Epoch);
        archive.Add(FrameKitConstants.ManifestJson, Encoding.UTF8.GetBytes(manifest.DeviceManifestJson(panelVersion)), PackageArchive.Regular);
        archive.Add(FrameKitConstants.ManifestEnv, Encoding.UTF8.GetBytes(manifest.ManifestEnv(panelVersion)), PackageArchive.Regular);
        foreach (var script in KitScripts)
        {
            archive.Add(script, PackageArchive.Lf(File.ReadAllBytes(Path.Combine(KitPaths.PackageScripts, script))), PackageArchive.Executable);
        }
        archive.Add(FrameKitConstants.SigningKey, Encoding.ASCII.GetBytes(trustedKey), PackageArchive.Regular);
        if (panelFiles.Count > 0)
        {
            archive.Add(FrameKitConstants.PanelKey, PackageArchive.Lf(File.ReadAllBytes(panelKey)), PackageArchive.Regular);
        }
        foreach (var (name, path) in panelFiles)
        {
            archive.Add(FrameKitConstants.PanelBinary + "/" + name, File.ReadAllBytes(path),
                        name == FrameKitConstants.PanelBinary ? PackageArchive.Executable : PackageArchive.Regular);
        }
        AddPayload(archive, payload, payload, manifest);
        archive.Add(FrameKitConstants.PackageFiles, Encoding.UTF8.GetBytes(string.Concat(archive.Files.Select(name => name + "\n"))),
                    PackageArchive.Regular);

        var outDir = Path.GetFullPath(settings.Out);
        Directory.CreateDirectory(outDir);
        var packagePath = Path.Combine(outDir, manifest.PackageFileName);
        var package = archive.ToGzip();
        File.WriteAllBytes(packagePath, package);
        var signature = key.Sign(package);
        if (!ReleaseKey.Verify(signingKey, package, signature))
        {
            throw new KitError("failed to verify new signature");
        }
        File.WriteAllBytes(packagePath + ".sig", signature);
        var digest = Convert.ToHexStringLower(SHA256.HashData(package));
        var indexPath = Path.Combine(outDir, manifest.IndexFileName);
        File.WriteAllText(indexPath, $"version={manifest.Version}\nfile={manifest.PackageFileName}\nsha256={digest}\n", new UTF8Encoding(false));

        Console.WriteLine($"package  {packagePath} ({package.Length / 1024} KB, sha256 {digest[..16]}...)");
        Console.WriteLine($"sig      {packagePath}.sig");
        Console.WriteLine($"index    {indexPath}");
        Console.WriteLine(settings.NextKey is null
            ? $"key      signed with {key.Fingerprint}"
            : $"key      key changed (old key: {key.Fingerprint}; new key: {trustedFingerprint})");
        Console.WriteLine(panelVersion.Length > 0 ? $"panel    {panelVersion}" : "panel    none (--no-panel)");
        return packagePath;
    }

    private static (string Version, List<(string Name, string Path)> Files) ReadPanel(string? panelDir, string panelKey)
    {
        if (panelDir is null)
        {
            Console.Error.WriteLine("warning: --no-panel: community apps panel is missing from build");
            return ("", []);
        }
        var files = new List<(string, string)>();
        foreach (var name in PanelFiles)
        {
            var path = Path.Combine(panelDir, name);
            if (!File.Exists(path) && PanelSignature.SignedFiles.Contains(name))
            {
                throw new KitError("shared panel is missing");
            }
            files.Add((name, path));
        }
        var version = File.ReadAllText(Path.Combine(panelDir, FrameKitConstants.PanelVersionFile)).Trim();
        if (!FrameAppManifest.VersionPattern().IsMatch(version))
        {
            throw new KitError($"panel VERSION '{version}' is not a valid version number");
        }
        if (!File.Exists(panelKey))
        {
            throw new KitError("panel key is missing");
        }
        if (PanelSignature.Problem(panelDir, File.ReadAllText(panelKey)) is { } problem)
        {
            throw new KitError(problem);
        }
        return (version, files);
    }

    private static void AddPayload(PackageArchive archive, string root, string directory, FrameAppManifest manifest)
    {
        var reserved = new HashSet<string>(KitScripts.Concat([FrameKitConstants.ManifestJson, FrameKitConstants.ManifestEnv, FrameKitConstants.SigningKey,
                                                              FrameKitConstants.PanelKey, FrameKitConstants.PackageFiles]), StringComparer.Ordinal);
        var executables = manifest.Executables.ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            if (file.EndsWith(".cm", StringComparison.Ordinal) || Path.GetFileName(file) == ".gitkeep")
            {
                continue;
            }
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            FrameAppManifest.CheckPayloadPath(relative);
            if (reserved.Contains(relative) || relative.StartsWith(FrameKitConstants.PanelBinary + "/", StringComparison.Ordinal))
            {
                throw new KitError($"payload file '{relative}' conflicts with a kit file");
            }
            var data = ReadPayloadFile(file, relative);
            var isHook = relative.StartsWith("hooks/", StringComparison.Ordinal);
            if (isHook || TextSuffixes.Contains(Path.GetExtension(relative)) || IsScript(data))
            {
                data = PackageArchive.Lf(data);
            }
            var executable = executables.Contains(relative) || relative.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) || isHook;
            archive.Add(relative, data, executable ? PackageArchive.Executable : PackageArchive.Regular);
        }
        foreach (var sub in Directory.GetDirectories(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            if (new DirectoryInfo(sub).LinkTarget is null)
            {
                AddPayload(archive, root, sub, manifest);
            }
        }
    }

    private static bool IsScript(byte[] data) =>
        data.Length >= 2 && data[0] == '#' && data[1] == '!' && Array.IndexOf(data, (byte)0) < 0;

    private static byte[] ReadPayloadFile(string file, string relative)
    {
        try
        {
            return File.ReadAllBytes(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var link = new FileInfo(file).LinkTarget;
            throw new KitError(link is null
                ? $"cannot read payload file '{relative}': {exception.Message}"
                : $"cannot resolve link to '{link}'");
        }
    }
}

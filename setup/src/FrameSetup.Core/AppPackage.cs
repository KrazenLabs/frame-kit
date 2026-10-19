using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FrameKit.Setup;

public sealed record AppManifest(
    string Id,
    string Name,
    string Version,
    string Description,
    string Publisher,
    string MinOsVersion,
    long FreeMb,
    bool RestartSteamVR,
    bool Service,
    bool Timer,
    IReadOnlyList<string> SteamVRDrivers,
    IReadOnlyList<string> DataDirs,
    bool HasUpdates,
    string PanelVersion,
    string SupportUrl);

public sealed partial class AppPackage
{
    public const string PackageResource = "app-package.tar.gz";

    private AppPackage(byte[] bytes, AppManifest manifest)
    {
        Bytes = bytes;
        Manifest = manifest;
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public AppManifest Manifest { get; }
    public byte[] Bytes { get; }
    public string Sha256 { get; }
    public string FileName => $"{Manifest.Id}-{Manifest.Version}{FrameKitConstants.PackageSuffix}";

    public static AppPackage Load(byte[] bytes)
    {
        using var compressed = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            var name = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;
            if (name != FrameKitConstants.ManifestJson || entry.DataStream is null)
            {
                continue;
            }
            using var reader = new StreamReader(entry.DataStream);
            return new AppPackage(bytes, ParseManifest(reader.ReadToEnd()));
        }
        throw new InvalidDataException($"The package has no {FrameKitConstants.ManifestJson}.");
    }

    public static AppPackage? FromAssembly(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(PackageResource);
        if (stream is null)
        {
            return null;
        }
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Load(copy.ToArray());
    }

    public static AppManifest ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("manifest.json isn't a JSON object.");
        }
        var id = Text(root, "id");
        if (!IdPattern().IsMatch(id))
        {
            throw new InvalidDataException($"manifest.json has an invalid id '{id}'.");
        }
        var version = Text(root, "version");
        if (!VersionPattern().IsMatch(version) || version.Length > FrameKitConstants.MaxVersionLength)
        {
            throw new InvalidDataException($"manifest.json has an invalid version '{version}'.");
        }
        var hasUpdates = root.TryGetProperty("updates", out var updates) && updates.ValueKind == JsonValueKind.Object &&
                         !string.IsNullOrEmpty(Text(updates, "index"));
        return new AppManifest(
            id,
            Fallback(Text(root, "name"), id),
            version,
            Text(root, "description"),
            Text(root, "publisher").Trim(),
            Fallback(Text(root, "min_os_version"), FrameKitConstants.DefaultMinOsVersion),
            root.TryGetProperty("free_mb", out var freeMb) && freeMb.ValueKind == JsonValueKind.Number &&
            freeMb.TryGetInt64(out var megabytes) && megabytes >= 0 ? megabytes : FrameKitConstants.DefaultFreeMb,
            root.TryGetProperty("restart_steamvr", out var restart) && restart.ValueKind == JsonValueKind.True,
            root.TryGetProperty("service", out var service) && service.ValueKind == JsonValueKind.True,
            root.TryGetProperty("timer", out var timer) && timer.ValueKind == JsonValueKind.True,
            List(root, "steamvr_drivers"),
            List(root, "data_dirs"),
            hasUpdates,
            Text(root, "panel_version"),
            SupportAddress(Text(root, "support_url")));
    }

    private static string SupportAddress(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "mailto" ? text : "";

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Fallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static IReadOnlyList<string> List(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }
        return value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").ToArray();
    }

    [GeneratedRegex(FrameKitConstants.IdPattern)]
    internal static partial Regex IdPattern();

    [GeneratedRegex(FrameKitConstants.VersionPattern)]
    internal static partial Regex VersionPattern();
}

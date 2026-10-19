using System.Security.Cryptography;
using System.Text;

namespace FrameKit;

public static class CommonModule
{
    public const string SumsFile = "SHA256SUMS";
    public const string VersionFile = "VERSION";
    public const string ReadmeFile = "README.md";
    public const string SourceFolder = "src";
    public const string NoteSuffix = ".cm";

    public static bool IsNote(string name) => name.EndsWith(NoteSuffix, StringComparison.Ordinal);

    public static IReadOnlyList<string> Files(string folder)
    {
        var files = new List<string>();
        foreach (var name in new[] { VersionFile, ReadmeFile })
        {
            if (File.Exists(Path.Combine(folder, name)))
            {
                files.Add(name);
            }
        }
        files.AddRange(SourceFolderFiles(folder).Where(name => !IsNote(name)));
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    public static IReadOnlyList<string> Notes(string folder) =>
        SourceFolderFiles(folder).Where(IsNote).Order(StringComparer.Ordinal).ToList();

    public static string Sums(string folder)
    {
        var text = new StringBuilder();
        foreach (var name in Files(folder))
        {
            text.Append(Hash(Path.Combine(folder, name))).Append("  ").Append(name).Append('\n');
        }
        return text.ToString();
    }

    public static string Version(string folder)
    {
        var path = Path.Combine(folder, VersionFile);
        if (!File.Exists(path))
        {
            throw new KitError($"{folder} has no {VersionFile}");
        }
        var version = File.ReadAllText(path).Trim();
        if (!FrameAppManifest.VersionPattern().IsMatch(version))
        {
            throw new KitError($"{path} has no {VersionFile}");
        }
        return version;
    }

    public static string Seal(string kitCommon)
    {
        var version = Version(kitCommon);
        var sumsPath = Path.Combine(kitCommon, SumsFile);
        var sums = Sums(kitCommon);
        if (File.Exists(sumsPath))
        {
            var sealedSums = ReadSums(sumsPath);
            var current = ParseSums(sums);
            if (sealedSums.TryGetValue(VersionFile, out var sealedVersion) && sealedVersion == current[VersionFile] &&
                !SameSums(sealedSums, current))
            {
                throw new KitError($"FrameKit common changed, but {Path.Combine(kitCommon, VersionFile)} still says {version}");
            }
        }
        File.WriteAllText(sumsPath, sums);
        return version;
    }

    public static string Copy(string kitCommon, string target)
    {
        var version = KitVersion(kitCommon);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            var sumsPath = Path.Combine(target, SumsFile);
            if (!File.Exists(sumsPath))
            {
                throw new KitError($"{target} is not empty");
            }
            if (Problems(target).Count > 0)
            {
                throw new KitError($"{target} has pending changes");
            }
            foreach (var name in ReadSums(sumsPath).Keys.Concat(Notes(target)))
            {
                File.Delete(Path.Combine(target, name));
            }
            File.Delete(sumsPath);
            DeleteEmptyFolders(target);
        }
        foreach (var name in Files(kitCommon).Concat(Notes(kitCommon)).Append(SumsFile))
        {
            var destination = Path.Combine(target, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(kitCommon, name), destination, overwrite: true);
        }
        return version;
    }

    public static string Check(string kitCommon, string target)
    {
        var kitVersion = KitVersion(kitCommon);
        var sumsPath = Path.Combine(target, SumsFile);
        if (!File.Exists(sumsPath))
        {
            throw new KitError($"{target} is not a copy of FrameKit common");
        }
        if (Problems(target).Count > 0)
        {
            throw new KitError($"{target} has pending changes");
        }
        var version = Version(target);
        if (version != kitVersion || !SameSums(ReadSums(sumsPath), ReadSums(Path.Combine(kitCommon, SumsFile))))
        {
            throw new KitError($"{target} has a different FrameKit common version");
        }
        return version;
    }

    public static List<string> Problems(string folder)
    {
        var problems = new List<string>();
        var listed = ReadSums(Path.Combine(folder, SumsFile));
        foreach (var (name, hash) in listed)
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
                problems.Add($"{name} is missing");
            }
            else if (Hash(path) != hash)
            {
                problems.Add($"{name} was changed");
            }
        }
        foreach (var name in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                     .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
                     .Where(name => name != SumsFile && !IsNote(name) && !listed.ContainsKey(name))
                     .Order(StringComparer.Ordinal))
        {
            problems.Add($"{name} is not listed");
        }
        return problems;
    }

    private static string KitVersion(string kitCommon)
    {
        var version = Version(kitCommon);
        var sumsPath = Path.Combine(kitCommon, SumsFile);
        if (!File.Exists(sumsPath) || Problems(kitCommon).Count > 0)
        {
            throw new KitError("the kit's FrameKit common has unregistered changes");
        }
        return version;
    }

    private static string Hash(string path)
    {
        var data = File.ReadAllBytes(path);
        var text = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == '\r' && i + 1 < data.Length && data[i + 1] == '\n')
            {
                continue;
            }
            text.Add(data[i]);
        }
        return Convert.ToHexStringLower(SHA256.HashData(text.ToArray()));
    }

    private static Dictionary<string, string> ReadSums(string path) => ParseSums(File.ReadAllText(path));

    private static Dictionary<string, string> ParseSums(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0))
        {
            var parts = line.Split("  ", 2);
            if (parts.Length != 2 || parts[0].Length != 64 || parts[1].Contains("..") || Path.IsPathRooted(parts[1]))
            {
                throw new KitError($"unexpected line in {SumsFile}: {line}");
            }
            if (!IsNote(parts[1]))
            {
                sums[parts[1]] = parts[0];
            }
        }
        return sums;
    }

    private static IEnumerable<string> SourceFolderFiles(string folder)
    {
        var sources = Path.Combine(folder, SourceFolder);
        return Directory.Exists(sources)
            ? Directory.EnumerateFiles(sources, "*", SearchOption.AllDirectories)
                       .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            : [];
    }

    private static bool SameSums(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var hash) && hash == entry.Value);

    private static void DeleteEmptyFolders(string folder)
    {
        foreach (var sub in Directory.EnumerateDirectories(folder))
        {
            DeleteEmptyFolders(sub);
            if (!Directory.EnumerateFileSystemEntries(sub).Any())
            {
                Directory.Delete(sub);
            }
        }
    }
}

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameKit.Setup;

public enum HostKeyCheck
{
    Known,
    New,
    Changed,
}

public sealed class KnownHeadset
{
    public string HostKey { get; set; } = "";
    public string Address { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Names { get; set; } = [];
    public DateTime LastUsed { get; set; }
}

[JsonSerializable(typeof(List<KnownHeadset>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class HeadsetJsonContext : JsonSerializerContext;

public sealed class HeadsetStore
{
    private const string FileName = "headsets.json";
    private const int MaxNames = 4;
    private readonly string _path;
    private readonly List<KnownHeadset> _headsets;

    private HeadsetStore(string path, List<KnownHeadset> headsets, string loadProblem)
    {
        _path = path;
        _headsets = headsets;
        LoadProblem = loadProblem;
    }

    public IReadOnlyList<KnownHeadset> Headsets => _headsets.OrderByDescending(h => h.LastUsed).ToList();

    public string LoadProblem { get; }

    public static HeadsetStore Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            return new HeadsetStore(path, [], "");
        }
        try
        {
            var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), HeadsetJsonContext.Default.ListKnownHeadset) ?? [];
            return new HeadsetStore(path, Merge(loaded), "");
        }
        catch (JsonException exception)
        {
            var backup = $"{path}.damaged-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(path, backup, overwrite: true);
            return new HeadsetStore(path, [],
                $"Failed to read list of known headsets ({exception.Message}). Searching network instead.");
        }
    }

    public HostKeyCheck Check(string hostKey, string expectedName)
    {
        if (IsKnown(hostKey))
        {
            return HostKeyCheck.Known;
        }
        return ClaimedBy(expectedName) is null ? HostKeyCheck.New : HostKeyCheck.Changed;
    }

    public bool IsKnown(string hostKey) => hostKey.Length > 0 && _headsets.Any(h => h.HostKey == hostKey);

    public KnownHeadset? ClaimedBy(string name) =>
        IsName(name)
            ? _headsets.Where(h => h.HostKey.Length > 0 && h.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                       .OrderByDescending(h => h.LastUsed)
                       .FirstOrDefault()
            : null;

    public void Remember(string address, string name, string hostKey)
    {
        var entry = hostKey.Length > 0
            ? _headsets.FirstOrDefault(h => h.HostKey == hostKey)
            : _headsets.FirstOrDefault(h => h.HostKey.Length == 0 && SameAddress(h.Address, address));
        if (entry is null)
        {
            entry = new KnownHeadset { HostKey = hostKey };
            _headsets.Add(entry);
        }
        entry.Address = address;
        AddName(entry, name);
        entry.LastUsed = DateTime.UtcNow;
        if (hostKey.Length > 0)
        {
            _headsets.RemoveAll(h => h.HostKey.Length == 0 && SameAddress(h.Address, address));
        }
        Save();
    }

    private static List<KnownHeadset> Merge(IEnumerable<KnownHeadset?> loaded)
    {
        var merged = new List<KnownHeadset>();
        foreach (var headset in loaded.OfType<KnownHeadset>().OrderBy(h => h.LastUsed))
        {
            headset.Names ??= [];
            var names = headset.Names.Prepend(headset.Name).ToList();
            var same = headset.HostKey.Length > 0 ? merged.FirstOrDefault(h => h.HostKey == headset.HostKey) : null;
            if (same is null)
            {
                same = new KnownHeadset { HostKey = headset.HostKey };
                merged.Add(same);
            }
            same.Address = headset.Address;
            same.LastUsed = headset.LastUsed;
            foreach (var name in names.AsEnumerable().Reverse())
            {
                AddName(same, name);
            }
        }
        return merged;
    }

    private static void AddName(KnownHeadset headset, string name)
    {
        if (!IsName(name))
        {
            return;
        }
        headset.Name = name;
        headset.Names.RemoveAll(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
        headset.Names.Insert(0, name);
        if (headset.Names.Count > MaxNames)
        {
            headset.Names.RemoveRange(MaxNames, headset.Names.Count - MaxNames);
        }
    }

    private static bool IsName(string? name) => !string.IsNullOrWhiteSpace(name) && !IPAddress.TryParse(name, out _);

    private static bool SameAddress(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(_headsets, HeadsetJsonContext.Default.ListKnownHeadset));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

using System.Formats.Tar;
using System.IO.Compression;

namespace FrameKit;

public sealed class PackageArchive : IDisposable
{
    public const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                           UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                           UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    public const UnixFileMode Regular = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const int GzipOsByte = 9;
    private const byte GzipUnknownOs = 255;
    private readonly MemoryStream _tar = new();
    private readonly TarWriter _writer;
    private readonly DateTimeOffset _time;
    private readonly HashSet<string> _files = new(StringComparer.Ordinal);
    private readonly List<string> _fileOrder = [];
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    public PackageArchive(long epoch)
    {
        _time = DateTimeOffset.FromUnixTimeSeconds(epoch);
        _writer = new TarWriter(_tar, TarEntryFormat.Gnu, leaveOpen: true);
    }

    public static byte[] Lf(byte[] data)
    {
        var output = new MemoryStream(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == '\r' && i + 1 < data.Length && data[i + 1] == '\n')
            {
                continue;
            }
            output.WriteByte(data[i]);
        }
        return output.ToArray();
    }

    public static byte[] SingleFileTarGz(string name, byte[] data, UnixFileMode mode, long epoch)
    {
        using var archive = new PackageArchive(epoch);
        archive.Add(name, data, mode);
        return archive.ToGzip();
    }

    public void Add(string name, byte[] data, UnixFileMode mode)
    {
        if (!_files.Add(name))
        {
            throw new KitError($"package already contains a file '{name}'");
        }
        _fileOrder.Add(name);
        var parts = name.Split('/');
        for (var i = 1; i < parts.Length; i++)
        {
            var directory = string.Join('/', parts[..i]);
            if (_directories.Add(directory))
            {
                _writer.WriteEntry(NewEntry(TarEntryType.Directory, directory + "/", Executable));
            }
        }
        var entry = NewEntry(TarEntryType.RegularFile, name, mode);
        entry.DataStream = new MemoryStream(data, writable: false);
        _writer.WriteEntry(entry);
    }

    public IReadOnlyList<string> Files => _fileOrder;

    public byte[] ToGzip()
    {
        _writer.Dispose();
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(_tar.GetBuffer(), 0, (int)_tar.Length);
        }
        var bytes = output.ToArray();
        bytes[GzipOsByte] = GzipUnknownOs;
        return bytes;
    }

    public void Dispose()
    {
        _writer.Dispose();
        _tar.Dispose();
    }

    private GnuTarEntry NewEntry(TarEntryType type, string name, UnixFileMode mode) => new(type, name)
    {
        Mode = mode,
        ModificationTime = _time,
        Uid = 0,
        Gid = 0,
        UserName = "",
        GroupName = "",
    };
}

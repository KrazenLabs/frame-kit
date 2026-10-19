using System.Net;
using System.Text;

namespace FrameKit.Setup;

public sealed record MdnsService(string Instance, string Host, int Port, IPAddress? Address);

public static class Mdns
{
    public const string DevkitService = "_steamos-devkit._tcp.local";
    private const ushort TypeA = 1;
    private const ushort TypePtr = 12;
    private const ushort TypeSrv = 33;
    private const ushort ClassIn = 1;
    private const ushort UnicastResponse = 0x8000;
    private const int MaxNameJumps = 32;

    private sealed record Record(string Name, ushort Type, byte[] Packet, int DataOffset, int DataLength);

    public static byte[] BuildQuery(string name, bool unicastResponse)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length is 0 or > 63)
            {
                throw new ArgumentException($"Invalid DNS label in '{name}'.", nameof(name));
            }
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        stream.WriteByte(0);
        WriteUInt16(stream, TypePtr);
        WriteUInt16(stream, (ushort)(ClassIn | (unicastResponse ? UnicastResponse : 0)));
        return stream.ToArray();
    }

    public static IReadOnlyList<MdnsService> ParseServices(IEnumerable<byte[]> packets, string service)
    {
        var records = new List<Record>();
        foreach (var packet in packets)
        {
            try
            {
                records.AddRange(ParseRecords(packet));
            }
            catch (FormatException)
            {
            }
        }
        var instances = records
            .Where(r => r.Type == TypePtr && Same(r.Name, service))
            .Select(r => TryReadName(r.Packet, r.DataOffset, out var target, out _) ? target : null)
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new List<MdnsService>();
        foreach (var instance in instances)
        {
            var srv = records.FirstOrDefault(r => r.Type == TypeSrv && Same(r.Name, instance) && r.DataLength >= 7);
            var host = "";
            var port = 0;
            if (srv is not null)
            {
                port = (srv.Packet[srv.DataOffset + 4] << 8) | srv.Packet[srv.DataOffset + 5];
                TryReadName(srv.Packet, srv.DataOffset + 6, out host, out _);
            }
            var a = records.FirstOrDefault(r => r.Type == TypeA && r.DataLength == 4 && host.Length > 0 && Same(r.Name, host));
            var address = a is null ? null : new IPAddress(a.Packet.AsSpan(a.DataOffset, 4));
            var label = instance.EndsWith("." + service, StringComparison.OrdinalIgnoreCase)
                ? instance[..^(service.Length + 1)]
                : instance;
            result.Add(new MdnsService(label, host.TrimEnd('.'), port, address));
        }
        return result;
    }

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('.'), b.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Record> ParseRecords(byte[] packet)
    {
        if (packet.Length < 12)
        {
            throw new FormatException("short packet");
        }
        var questions = ReadUInt16(packet, 4);
        var total = ReadUInt16(packet, 6) + ReadUInt16(packet, 8) + ReadUInt16(packet, 10);
        var offset = 12;
        for (var i = 0; i < questions; i++)
        {
            if (!TryReadName(packet, offset, out _, out offset) || offset + 4 > packet.Length)
            {
                throw new FormatException("bad question");
            }
            offset += 4;
        }
        var records = new List<Record>();
        for (var i = 0; i < total; i++)
        {
            if (!TryReadName(packet, offset, out var name, out offset) || offset + 10 > packet.Length)
            {
                throw new FormatException("bad record");
            }
            var type = ReadUInt16(packet, offset);
            var length = ReadUInt16(packet, offset + 8);
            offset += 10;
            if (offset + length > packet.Length)
            {
                throw new FormatException("record overruns packet");
            }
            records.Add(new Record(name, type, packet, offset, length));
            offset += length;
        }
        return records;
    }

    private static bool TryReadName(byte[] packet, int start, out string name, out int next)
    {
        var labels = new List<string>();
        var offset = start;
        next = -1;
        for (var jumps = 0; jumps <= MaxNameJumps;)
        {
            if (offset >= packet.Length)
            {
                break;
            }
            var length = packet[offset];
            if (length == 0)
            {
                if (next < 0)
                {
                    next = offset + 1;
                }
                name = string.Join('.', labels);
                return true;
            }
            if ((length & 0xC0) == 0xC0)
            {
                if (offset + 1 >= packet.Length)
                {
                    break;
                }
                if (next < 0)
                {
                    next = offset + 2;
                }
                offset = ((length & 0x3F) << 8) | packet[offset + 1];
                jumps++;
                continue;
            }
            if ((length & 0xC0) != 0 || offset + 1 + length > packet.Length)
            {
                break;
            }
            labels.Add(Encoding.UTF8.GetString(packet, offset + 1, length));
            offset += 1 + length;
        }
        name = "";
        next = start;
        return false;
    }

    private static ushort ReadUInt16(byte[] packet, int offset) => (ushort)((packet[offset] << 8) | packet[offset + 1]);

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value & 0xFF));
    }
}

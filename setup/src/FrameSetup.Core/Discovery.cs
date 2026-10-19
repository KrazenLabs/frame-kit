using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace FrameKit.Setup;

public sealed record HeadsetCandidate(string Address, string Name, string Source, bool LooksLikeFrame = false)
{
    public string Label
    {
        get
        {
            var label = Name.Length > 0 && !string.Equals(Name, Address, StringComparison.OrdinalIgnoreCase) ? $"{Name} · {Address}" : Address;
            return LooksLikeFrame ? label : $"{label} · may not be a Steam Frame";
        }
    }
}

public static class Discovery
{
    public const int DevkitPort = 32000;
    public const int FrameHintPort = 8088;
    public const string DongleAddress = "10.35.78.1";
    private const int MdnsPort = 5353;
    private static readonly IPAddress MdnsGroup = IPAddress.Parse("224.0.0.251");
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(2);
    private const int ScanParallelism = 128;
    private static readonly TimeSpan NameTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan FrameHintTimeout = TimeSpan.FromSeconds(1.5);

    public static async Task<IReadOnlyList<HeadsetCandidate>> FindAsync(IReadOnlyCollection<string> remembered, IReadOnlyCollection<string> connectedBefore,
        TimeSpan mdnsWindow, CancellationToken cancellation)
    {
        var found = new ConcurrentDictionary<string, HeadsetCandidate>(StringComparer.OrdinalIgnoreCase);
        var direct = remembered.Append(DongleAddress).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tasks = new List<Task>
        {
            BrowseMdnsAsync(found, mdnsWindow, cancellation),
            ScanSubnetsAsync(found, cancellation),
            Task.WhenAll(direct.Select(async address =>
            {
                if (await IsDevkitAsync(address, cancellation).ConfigureAwait(false))
                {
                    found.TryAdd(address, new HeadsetCandidate(address, "", address == DongleAddress ? "dongle" : "remembered"));
                }
            })),
        };
        await Task.WhenAll(tasks).ConfigureAwait(false);
        var described = await Task.WhenAll(found.Values.Select(c => DescribeAsync(c, cancellation))).ConfigureAwait(false);
        return Merge(described, connectedBefore);
    }

    private static async Task<HeadsetCandidate> DescribeAsync(HeadsetCandidate candidate, CancellationToken cancellation)
    {
        var named = NameAsync(candidate, cancellation);
        var hint = PortOpenAsync(candidate.Address, FrameHintPort, FrameHintTimeout, cancellation);
        return await named.ConfigureAwait(false) with { LooksLikeFrame = await hint.ConfigureAwait(false) };
    }

    public static IReadOnlyList<HeadsetCandidate> Merge(IEnumerable<HeadsetCandidate> candidates, IReadOnlyCollection<string> connectedBefore)
    {
        var list = candidates.ToList();
        var onNetwork = list.Where(c => c.Source != "dongle").ToList();
        return (onNetwork.Count > 0 ? onNetwork : list)
            .OrderByDescending(c => c.LooksLikeFrame)
            .ThenByDescending(c => IsIn(connectedBefore, c.Address))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static HeadsetCandidate? PickAutomatically(IReadOnlyList<HeadsetCandidate> found, IReadOnlyCollection<string> connectedBefore,
        IReadOnlyCollection<string> skipped)
    {
        var open = found.Where(c => !IsIn(skipped, c.Address)).ToList();
        var frames = open.Where(c => c.LooksLikeFrame).ToList();
        if (frames.Count == 1)
        {
            return frames[0];
        }
        var known = (frames.Count > 1 ? frames : open).Where(c => IsIn(connectedBefore, c.Address)).ToList();
        return known.Count == 1 ? known[0] : null;
    }

    private static bool IsIn(IReadOnlyCollection<string> addresses, string address) =>
        addresses.Contains(address, StringComparer.OrdinalIgnoreCase);

    public static async Task<bool> IsDevkitAsync(string address, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(HttpTimeout);
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var body = await http.GetStringAsync(new Uri($"http://{FormatHost(address)}:{DevkitPort}/properties.json"), timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   (document.RootElement.TryGetProperty("devkit1", out _) || document.RootElement.TryGetProperty("login", out _));
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or UriFormatException or SocketException)
        {
            return false;
        }
    }

    public static string FormatHost(string address) =>
        IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address;

    public static IReadOnlyList<(IPAddress Address, int PrefixLength)> LocalNetworks(bool withGatewayOnly)
    {
        var networks = new List<(IPAddress, int)>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }
            var properties = adapter.GetIPProperties();
            if (withGatewayOnly && !properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            {
                continue;
            }
            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                {
                    continue;
                }
                var bytes = address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    continue;
                }
                networks.Add((address, unicast.PrefixLength));
            }
        }
        return networks;
    }

    public static IEnumerable<IPAddress> ScanTargets(IPAddress own, int prefixLength)
    {
        var prefix = Math.Clamp(prefixLength, 24, 30);
        var value = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(own.GetAddressBytes(), 0));
        var mask = prefix == 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix);
        var network = value & mask;
        var broadcast = network | ~mask;
        for (var host = network + 1; host < broadcast; host++)
        {
            if (host == value)
            {
                continue;
            }
            yield return new IPAddress(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)host)));
        }
    }

    private static async Task ScanSubnetsAsync(ConcurrentDictionary<string, HeadsetCandidate> found, CancellationToken cancellation)
    {
        var targets = LocalNetworks(withGatewayOnly: true).SelectMany(n => ScanTargets(n.Address, n.PrefixLength)).Distinct().ToList();
        using var gate = new SemaphoreSlim(ScanParallelism);
        await Task.WhenAll(targets.Select(async target =>
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (await PortOpenAsync(target.ToString(), DevkitPort, ProbeTimeout, cancellation).ConfigureAwait(false) &&
                    await IsDevkitAsync(target.ToString(), cancellation).ConfigureAwait(false))
                {
                    found.TryAdd(target.ToString(), new HeadsetCandidate(target.ToString(), "", "network"));
                }
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);
    }

    private static async Task<bool> PortOpenAsync(string host, int port, TimeSpan limit, CancellationToken cancellation)
    {
        using var client = IPAddress.TryParse(host, out var ip) ? new TcpClient(ip.AddressFamily) : new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(limit);
        try
        {
            if (ip is not null)
            {
                await client.ConnectAsync(ip, port, timeout.Token).ConfigureAwait(false);
            }
            else
            {
                await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            }
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            cancellation.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static async Task BrowseMdnsAsync(ConcurrentDictionary<string, HeadsetCandidate> found, TimeSpan window, CancellationToken cancellation)
    {
        var packets = new ConcurrentBag<byte[]>();
        var listeners = new List<Task>();
        var sockets = new List<UdpClient>();
        try
        {
            foreach (var (address, _) in LocalNetworks(withGatewayOnly: false))
            {
                try
                {
                    var client = new UdpClient(new IPEndPoint(address, 0));
                    client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                    sockets.Add(client);
                    var query = Mdns.BuildQuery(Mdns.DevkitService, unicastResponse: true);
                    await client.SendAsync(query, new IPEndPoint(MdnsGroup, MdnsPort), cancellation).ConfigureAwait(false);
                    listeners.Add(ReceiveAsync(client, packets, window, cancellation));
                }
                catch (SocketException)
                {
                }
            }
            await Task.WhenAll(listeners).ConfigureAwait(false);
        }
        finally
        {
            foreach (var socket in sockets)
            {
                socket.Dispose();
            }
        }
        foreach (var service in Mdns.ParseServices(packets, Mdns.DevkitService))
        {
            if (service.Address is null)
            {
                continue;
            }
            var address = service.Address.ToString();
            var name = !string.IsNullOrWhiteSpace(service.Instance) ? service.Instance : service.Host;
            found.AddOrUpdate(address, new HeadsetCandidate(address, name, "mdns"),
                (_, existing) => existing with { Name = string.IsNullOrWhiteSpace(existing.Name) ? name : existing.Name });
        }
    }

    private static async Task ReceiveAsync(UdpClient client, ConcurrentBag<byte[]> packets, TimeSpan window, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(window);
        try
        {
            while (true)
            {
                var result = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                packets.Add(result.Buffer);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    private static async Task<HeadsetCandidate> NameAsync(HeadsetCandidate candidate, CancellationToken cancellation)
    {
        if (!string.IsNullOrWhiteSpace(candidate.Name))
        {
            return candidate;
        }
        try
        {
            var lookup = Dns.GetHostEntryAsync(candidate.Address, cancellation);
            if (await Task.WhenAny(lookup, Task.Delay(NameTimeout, cancellation)).ConfigureAwait(false) != lookup)
            {
                return candidate with { Name = candidate.Address };
            }
            var entry = await lookup.ConfigureAwait(false);
            var host = entry.HostName.Split('.')[0];
            if (!string.IsNullOrWhiteSpace(host) && !IPAddress.TryParse(entry.HostName, out _))
            {
                return candidate with { Name = host };
            }
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ArgumentException)
        {
        }
        return candidate with { Name = candidate.Address };
    }
}

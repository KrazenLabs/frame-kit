using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace FrameKit.Setup;

public sealed record CommandResult(int ExitCode, IReadOnlyList<string> Lines, bool TimedOut = false);

public enum ConnectFailure
{
    NotAuthorized,
    Unreachable,
    HostKeyChanged,
}

public sealed class ConnectException : Exception
{
    public ConnectException(ConnectFailure failure, string message, Exception? inner = null) : base(message, inner)
    {
        Failure = failure;
    }

    public ConnectFailure Failure { get; }
}

public sealed class FrameConnection : IDisposable
{
    public const string DefaultUser = "steamos";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(3);
    private readonly ConnectionInfo _info;
    private readonly SshClient _ssh;

    private FrameConnection(string address, ConnectionInfo info, SshClient ssh, string hostKey)
    {
        Address = address;
        _info = info;
        _ssh = ssh;
        HostKey = hostKey;
    }

    public string Address { get; }
    public string HostKey { get; }

    public static async Task<FrameConnection> ConnectAsync(string address, string expectedName, SshKey key, HeadsetStore store,
        bool acceptChangedKey, CancellationToken cancellation, string user = DefaultUser)
    {
        var keyFile = new PrivateKeyFile(new MemoryStream(Encoding.ASCII.GetBytes(key.PrivateKeyText)));
        var info = new ConnectionInfo(address, 22, user, new PrivateKeyAuthenticationMethod(user, keyFile))
        {
            Timeout = ConnectTimeout,
        };
        var ssh = new SshClient(info) { KeepAliveInterval = KeepAliveInterval };
        var seen = "";
        var mismatch = false;
        ssh.HostKeyReceived += (_, e) =>
        {
            seen = Fingerprint(e);
            mismatch = store.Check(seen, expectedName) == HostKeyCheck.Changed && !acceptChangedKey;
            e.CanTrust = !mismatch;
        };
        try
        {
            await ssh.ConnectAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            ssh.Dispose();
            if (mismatch)
            {
                var expected = store.ClaimedBy(expectedName)?.HostKey ?? "";
                throw new ConnectException(ConnectFailure.HostKeyChanged,
                    $"{expectedName} at {address} identifies itself differently than last time (expected {expected}, got {seen}). " +
                    "If you have reset it, select \"Trust this headset\".",
                    exception);
            }
            if (exception is SshAuthenticationException)
            {
                throw new ConnectException(ConnectFailure.NotAuthorized, "This PC is not yet paired with the headset.", exception);
            }
            throw new ConnectException(ConnectFailure.Unreachable, $"Cannot connect to the headset at {address} ({exception.Message}).", exception);
        }
        return new FrameConnection(address, info, ssh, seen);
    }

    public async Task<CommandResult> RunAsync(string command, Action<string>? onLine, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        using var sshCommand = _ssh.CreateCommand(WithErrorsInOutput(command));
        var lines = new List<string>();
        var execute = sshCommand.ExecuteAsync(cancellation);
        var reading = Task.WhenAll(ReadLinesAsync(sshCommand.OutputStream, lines, onLine, cancellation),
                                   ReadLinesAsync(sshCommand.ExtendedOutputStream, lines, onLine, cancellation));
        if (timeout is { } limit && await Task.WhenAny(execute, Task.Delay(limit, cancellation)).ConfigureAwait(false) != execute)
        {
            cancellation.ThrowIfCancellationRequested();
            _ = Task.WhenAll(execute, reading).ContinueWith(abandoned => abandoned.Exception, TaskScheduler.Default);
            lock (lines)
            {
                return new CommandResult(-1, lines.ToList(), TimedOut: true);
            }
        }
        await execute.ConfigureAwait(false);
        await Task.WhenAny(reading, Task.Delay(OutputDrainTimeout, cancellation)).ConfigureAwait(false);
        lock (lines)
        {
            return new CommandResult(sshCommand.ExitStatus ?? -1, lines.ToList());
        }
    }

    public static string WithErrorsInOutput(string command) => "{ " + command + "\n} 2>&1";

    private static Task ReadLinesAsync(Stream stream, List<string> lines, Action<string>? onLine, CancellationToken cancellation) =>
        Task.Run(async () =>
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
            {
                lock (lines)
                {
                    lines.Add(line);
                }
                onLine?.Invoke(line);
            }
        }, cancellation);

    public async Task UploadAsync(byte[] data, string remotePath, CancellationToken cancellation)
    {
        using var sftp = new SftpClient(_info);
        var seen = "";
        sftp.HostKeyReceived += (_, e) =>
        {
            seen = Fingerprint(e);
            e.CanTrust = string.Equals(seen, HostKey, StringComparison.Ordinal);
        };
        try
        {
            await sftp.ConnectAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && seen.Length > 0 &&
                                          !string.Equals(seen, HostKey, StringComparison.Ordinal))
        {
            throw new ConnectException(ConnectFailure.HostKeyChanged,
                $"File transfer to {HostKey} was redirected to {seen}. Aborting. Please check your network.", exception);
        }
        using var stream = new MemoryStream(data, writable: false);
        await Task.Run(() => sftp.UploadFile(stream, remotePath, canOverride: false), cancellation).ConfigureAwait(false);
        sftp.Disconnect();
    }

    private static string Fingerprint(HostKeyEventArgs e) => "SHA256:" + e.FingerPrintSHA256;

    public void Dispose()
    {
        if (_ssh.IsConnected)
        {
            _ssh.Disconnect();
        }
        _ssh.Dispose();
    }
}

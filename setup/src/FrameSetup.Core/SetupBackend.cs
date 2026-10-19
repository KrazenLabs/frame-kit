namespace FrameKit.Setup;

public interface ISetupBackend
{
    SshKey LoadKey(string configDirectory);
    void ResetKey(string configDirectory);
    HeadsetStore LoadStore(string configDirectory);
    Task<IReadOnlyList<HeadsetCandidate>> FindAsync(IReadOnlyCollection<string> remembered, IReadOnlyCollection<string> connectedBefore,
        CancellationToken cancellation);
    Task<bool> IsDevkitAsync(string address, CancellationToken cancellation);
    Task<PairingAttempt> PairAsync(string address, string publicKeyLine, IProgress<PairingAttempt> progress, CancellationToken cancellation);
    Task<IHeadsetSession> ConnectAsync(string address, string expectedName, SshKey key, HeadsetStore store, bool acceptChangedKey,
        CancellationToken cancellation);
}

public interface IHeadsetSession : IDisposable
{
    string Address { get; }
    string HostKey { get; }
    Task<DeviceInfo> ProbeAsync(string appId, CancellationToken cancellation);
    Task<OperationResult> InstallAsync(AppPackage package, bool autoUpdate, bool restartSteamVR, Action<string>? onLine,
        CancellationToken cancellation);
    Task<OperationResult> UninstallAsync(string appId, bool purge, Action<string>? onLine, CancellationToken cancellation);
    Task<UnpairResult> UnpairAsync(SshKey key, CancellationToken cancellation);
    Task<OperationResult> RestartSteamVRAsync(Action<string>? onLine, CancellationToken cancellation);
    Task<string> DiagnosticsAsync(AppManifest manifest, CancellationToken cancellation);
}

public sealed class HeadsetBackend : ISetupBackend
{
    private static readonly TimeSpan MdnsWindow = TimeSpan.FromSeconds(2);

    public static HeadsetBackend Instance { get; } = new();

    public SshKey LoadKey(string configDirectory) => SshKey.LoadOrCreate(configDirectory);

    public void ResetKey(string configDirectory) => SshKey.SetAside(configDirectory);

    public HeadsetStore LoadStore(string configDirectory) => HeadsetStore.Load(configDirectory);

    public Task<IReadOnlyList<HeadsetCandidate>> FindAsync(IReadOnlyCollection<string> remembered, IReadOnlyCollection<string> connectedBefore,
        CancellationToken cancellation) => Discovery.FindAsync(remembered, connectedBefore, MdnsWindow, cancellation);

    public Task<bool> IsDevkitAsync(string address, CancellationToken cancellation) => Discovery.IsDevkitAsync(address, cancellation);

    public Task<PairingAttempt> PairAsync(string address, string publicKeyLine, IProgress<PairingAttempt> progress, CancellationToken cancellation) =>
        Pairing.PairAsync(address, publicKeyLine, progress, cancellation);

    public async Task<IHeadsetSession> ConnectAsync(string address, string expectedName, SshKey key, HeadsetStore store,
        bool acceptChangedKey, CancellationToken cancellation) =>
        new SshHeadsetSession(await FrameConnection.ConnectAsync(address, expectedName, key, store, acceptChangedKey, cancellation)
            .ConfigureAwait(false));

    private sealed class SshHeadsetSession(FrameConnection connection) : IHeadsetSession
    {
        public string Address => connection.Address;
        public string HostKey => connection.HostKey;

        public Task<DeviceInfo> ProbeAsync(string appId, CancellationToken cancellation) => DeviceProbe.RunAsync(connection, appId, cancellation);

        public Task<OperationResult> InstallAsync(AppPackage package, bool autoUpdate, bool restartSteamVR, Action<string>? onLine,
            CancellationToken cancellation) => SetupOperations.InstallAsync(connection, package, autoUpdate, restartSteamVR, onLine, cancellation);

        public Task<OperationResult> UninstallAsync(string appId, bool purge, Action<string>? onLine, CancellationToken cancellation) =>
            SetupOperations.UninstallAsync(connection, appId, purge, onLine, cancellation);

        public Task<UnpairResult> UnpairAsync(SshKey key, CancellationToken cancellation) => SetupOperations.UnpairAsync(connection, key, cancellation);

        public Task<OperationResult> RestartSteamVRAsync(Action<string>? onLine, CancellationToken cancellation) =>
            SetupOperations.RestartSteamVRAsync(connection, onLine, cancellation);

        public Task<string> DiagnosticsAsync(AppManifest manifest, CancellationToken cancellation) =>
            SetupOperations.DiagnosticsAsync(connection, manifest, cancellation);

        public void Dispose() => connection.Dispose();
    }
}

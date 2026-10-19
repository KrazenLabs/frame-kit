using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FrameKit.Setup.App;

public enum SetupStep
{
    Welcome,
    Connect,
    Ready,
    Working,
    Done,
    Broken,
}

public sealed partial class SetupViewModel : ObservableObject
{
    private readonly ISetupBackend _backend;
    private readonly AppPackage? _package;
    private readonly SetupBranding _branding;
    private readonly string _configDirectory;
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _connectLoop;
    private IHeadsetSession? _connection;
    private HeadsetStore? _store;
    private Window? _view;
    private string _connectedAddress = "";
    private string? _chosenAddress;
    private string? _lastPreferred;
    private string? _acceptChangedKeyAt;
    private string? _probedAddress;
    private SshKey? _key;
    private readonly HashSet<string> _skipped = new(StringComparer.OrdinalIgnoreCase);
    private bool _updatingHeadsetList;
    private string _workingOn = "";
    private string _installedBefore = "";
    private bool _closeAnyway;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsConnect), nameof(IsReady), nameof(IsWorking), nameof(IsDone), nameof(IsBroken),
                              nameof(IsWelcomePage), nameof(IsNotices))]
    private SetupStep _step;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcomePage), nameof(IsNotices), nameof(NoticesText), nameof(NoticesLines))]
    private bool _showingNotices;

    public const string NoticesResource = "third-party-notices.txt";
    private string? _notices;

    [ObservableProperty] private bool _autoUpdate = true;
    [ObservableProperty] private string _connectStatus = "";
    [ObservableProperty] private string _connectDetail = "";
    [ObservableProperty] private bool _connectBusy;
    [ObservableProperty] private bool _showManual;
    [ObservableProperty] private string _manualAddress = "";
    [ObservableProperty] private string _manualError = "";
    [ObservableProperty] private bool _showForget;
    [ObservableProperty] private bool _showRetry;
    [ObservableProperty] private bool _showResetKey;
    [ObservableProperty] private HeadsetCandidate? _selectedHeadset;
    [ObservableProperty] private bool _showHeadsetChoice;
    [ObservableProperty] private string _headsetName = "";
    [ObservableProperty] private string _headsetDetails = "";
    [ObservableProperty] private string _installedText = "";
    [ObservableProperty] private string _readyNote = "";
    [ObservableProperty] private string _primaryActionText = "Install";
    [ObservableProperty] private bool _canInstall;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReadyActions))]
    private bool _confirmingUninstall;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReadyActions))]
    private bool _confirmingUnpair;

    [ObservableProperty] private string _unpairQuestion = "";
    [ObservableProperty] private bool _purgeData;
    [ObservableProperty] private bool _restartSteamVRAfter;
    [ObservableProperty] private string _workingTitle = "";
    [ObservableProperty] private bool _confirmingClose;
    [ObservableProperty] private string _closeQuestion = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _doneSuccess;
    [ObservableProperty] private string _doneTitle = "";
    [ObservableProperty] private string _doneMessage = "";
    [ObservableProperty] private string _doneHint = "";
    [ObservableProperty] private bool _showDevModeNote;
    [ObservableProperty] private bool _offerSteamVRRestart;
    [ObservableProperty] private bool _showCopyDetails;
    [ObservableProperty] private bool _showReportLink;
    [ObservableProperty] private string _copyDetailsText = "Copy details";
    [ObservableProperty] private string _brokenMessage = "";

    public SetupViewModel(AppPackage? package, SetupBranding branding, string configDirectory, string brokenMessage,
        ISetupBackend? backend = null)
    {
        _backend = backend ?? HeadsetBackend.Instance;
        _package = package;
        _branding = branding;
        _configDirectory = configDirectory;
        Icon = AppIcon.Load(branding.Icon);
        if (package is null || brokenMessage.Length > 0)
        {
            BrokenMessage = brokenMessage.Length > 0 ? brokenMessage : "Could not find app package.";
            Step = SetupStep.Broken;
        }
    }

    public ObservableCollection<HeadsetCandidate> Headsets { get; } = new();
    public TimeSpan SearchPause { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RetryPause { get; init; } = TimeSpan.FromSeconds(4);
    public Func<string, Task>? CopyToClipboard { get; set; }
    public AppManifest? Manifest => _package?.Manifest;
    public string AppName => Manifest?.Name ?? "FrameKit";
    public string AppVersion => Manifest?.Version ?? "";
    public string AppDescription => Manifest?.Description ?? "";
    public string AppSubtitle => Manifest is null ? "" :
        Manifest.Publisher.Length > 0 ? $"Version {Manifest.Version} · by {Manifest.Publisher}" : $"Version {Manifest.Version}";
    public string WindowTitle => $"{AppName} Setup";
    public bool OffersAutoUpdate => Manifest?.HasUpdates == true;
    public bool HasDataDirs => Manifest?.DataDirs.Count > 0;
    public Uri? SupportUri => Manifest?.SupportUrl is { Length: > 0 } url ? new Uri(url) : null;
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    public bool AppNeedsSteamVRRestart => Manifest?.RestartSteamVR == true;
    public string WelcomeText => $"This setup will install {AppName} on your Steam Frame.";
    public string PairingStepText => $"You will get a pairing request from \"{SshKey.Comment}\". Accept it to continue the setup.";
    public string DevModeNote => "You can now turn Developer Mode off.";
    public string UninstallQuestion => $"Remove {AppName} from the headset?";
    public string CloseNote => "Setup might not finish correctly.";
    public bool IsWelcome => Step == SetupStep.Welcome;
    public bool IsWelcomePage => IsWelcome && !ShowingNotices;
    public bool IsNotices => IsWelcome && ShowingNotices;
    public string NoticesText => ShowingNotices ? _notices ??= LoadNotices() : "";
    public IReadOnlyList<string> NoticesLines =>
        ShowingNotices ? NoticesText.Split('\n').Select(line => line.Length == 0 ? " " : line).ToArray() : [];
    public bool IsConnect => Step == SetupStep.Connect;
    public bool IsReady => Step == SetupStep.Ready;
    public bool IsWorking => Step == SetupStep.Working;
    public bool IsDone => Step == SetupStep.Done;
    public bool IsBroken => Step == SetupStep.Broken;
    public bool ShowReadyActions => !ConfirmingUninstall && !ConfirmingUnpair;

    public static SetupViewModel Create(string[] args)
    {
        var assembly = typeof(SetupViewModel).Assembly;
        AppPackage? package = null;
        var broken = "";
        try
        {
            var index = Array.IndexOf(args, "--package");
            package = index >= 0 && index + 1 < args.Length
                ? AppPackage.Load(File.ReadAllBytes(args[index + 1]))
                : AppPackage.FromAssembly(assembly);
            if (package is null)
            {
                broken = "Could not find app package.";
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            broken = "App package is damaged: " + exception.Message;
        }
        return new SetupViewModel(package, SetupBranding.FromAssembly(assembly), SetupPaths.ConfigDirectory, broken);
    }

    public void AttachView(Window window)
    {
        _view = window;
        window.Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (Step == SetupStep.Working && !_closeAnyway && !e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            CloseQuestion = $"Setup is still {_workingOn}. Close it anyway?";
            ConfirmingClose = true;
            return;
        }
        Shutdown();
    }

    [RelayCommand]
    private void KeepWaiting() => ConfirmingClose = false;

    [RelayCommand]
    private void CloseAnyway()
    {
        _closeAnyway = true;
        _view?.Close();
    }

    public void Shutdown()
    {
        _connectLoop?.Cancel();
        _connection?.Dispose();
        _connection = null;
    }

    [RelayCommand]
    private void Continue()
    {
        Step = SetupStep.Connect;
        StartConnecting(null);
    }

    [RelayCommand]
    private void ShowNotices() => ShowingNotices = true;

    [RelayCommand]
    private void CloseNotices() => ShowingNotices = false;

    public static string LoadNotices()
    {
        using var stream = typeof(SetupViewModel).Assembly.GetManifestResourceStream(NoticesResource);
        if (stream is null)
        {
            return "This Setup was built without third-party notices.";
        }
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [RelayCommand]
    private void ToggleManual()
    {
        ShowManual = !ShowManual;
        ManualError = "";
    }

    [RelayCommand]
    private void ConnectManual()
    {
        var address = ManualAddress.Trim();
        if (address.Length == 0 || address.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':')))
        {
            ManualError = "Enter an IP address (e.g. 192.168.1.50) or host name.";
            return;
        }
        ManualError = "";
        Choose(address);
        StartConnecting(address);
    }

    [RelayCommand]
    private void ForgetHeadset()
    {
        _acceptChangedKeyAt = _connectedAddress.Length > 0 ? _connectedAddress : null;
        ShowForget = false;
        StartConnecting(_connectedAddress.Length > 0 ? _connectedAddress : null);
    }

    [RelayCommand]
    private void OtherHeadset()
    {
        if (_connectedAddress.Length > 0)
        {
            _skipped.Add(_connectedAddress);
        }
        _connection?.Dispose();
        _connection = null;
        _connectedAddress = "";
        _chosenAddress = null;
        ShowSelected(null);
        ConfirmingUninstall = false;
        ConfirmingUnpair = false;
        Step = SetupStep.Connect;
        StartConnecting(null);
    }

    partial void OnSelectedHeadsetChanged(HeadsetCandidate? value)
    {
        if (_updatingHeadsetList || value is null || Step != SetupStep.Connect || _connection is not null)
        {
            return;
        }
        Choose(value.Address);
        if (value.Address != _connectedAddress)
        {
            StartConnecting(value.Address);
        }
    }

    private void Choose(string address)
    {
        _chosenAddress = address;
        _skipped.Remove(address);
    }

    private void ShowSelected(HeadsetCandidate? candidate)
    {
        _updatingHeadsetList = true;
        SelectedHeadset = candidate is null ? null : Headsets.FirstOrDefault(h => h.Address == candidate.Address);
        _updatingHeadsetList = false;
    }

    [RelayCommand]
    private void RetryConnecting() => StartConnecting(_lastPreferred);

    [RelayCommand]
    private void ResetKey()
    {
        try
        {
            _backend.ResetKey(_configDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ConnectDetail = $"Error: {exception.Message}\nCannot remove old key. Please delete the folder {_configDirectory} and restart the installer.";
            ShowResetKey = false;
            return;
        }
        StartConnecting(_lastPreferred);
    }

    private void StartConnecting(string? preferred)
    {
        _lastPreferred = preferred;
        _connectLoop?.Cancel();
        _connectLoop = new CancellationTokenSource();
        _ = ConnectLoopAsync(preferred, _connectLoop.Token);
    }

    private async Task ConnectLoopAsync(string? preferred, CancellationToken cancellation)
    {
        ConnectBusy = true;
        ShowForget = false;
        ShowRetry = false;
        ShowResetKey = false;
        try
        {
            ConnectStatus = "Getting ready…";
            ConnectDetail = "";
            SshKey key;
            try
            {
                key = await Task.Run(() => _backend.LoadKey(_configDirectory), cancellation);
                _key = key;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ConnectStatus = "Could not read the pairing key";
                ConnectDetail = SshKey.IsOverridden
                    ? $"Error: {exception.Message}\nIt's the key that {SshKey.KeyOverrideVariable} names."
                    : $"Error: {exception.Message}\nSteam Frame pairing key might be corrupted or not readable.";
                ShowResetKey = !SshKey.IsOverridden;
                ShowRetry = true;
                return;
            }
            try
            {
                _store = _backend.LoadStore(_configDirectory);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ConnectStatus = "Cannot read setup files";
                ConnectDetail = $"Error: {exception.Message}\nPlease make sure the installer has permission to read {_configDirectory}.";
                ShowRetry = true;
                return;
            }
            var searchedBefore = false;
            while (!cancellation.IsCancellationRequested)
            {
                var target = preferred;
                if (target is null)
                {
                    if (!searchedBefore)
                    {
                        ConnectStatus = "Searching network for your Steam Frame…";
                        searchedBefore = true;
                    }
                    var remembered = _store.Headsets.Select(h => h.Address).ToList();
                    var connectedBefore = _store.Headsets.Where(h => h.HostKey.Length > 0).Select(h => h.Address).ToList();
                    var found = await _backend.FindAsync(remembered, connectedBefore, cancellation);
                    UpdateHeadsetList(found);
                    var chosen = _chosenAddress is null ? null : found.FirstOrDefault(c => string.Equals(c.Address, _chosenAddress, StringComparison.OrdinalIgnoreCase));
                    var pick = chosen ?? Discovery.PickAutomatically(found, connectedBefore, _skipped);
                    ShowHeadsetChoice = found.Count > 1 || (found.Count > 0 && pick is null);
                    ShowSelected(pick);
                    if (pick is null)
                    {
                        DescribeChoice(found);
                        await Task.Delay(SearchPause, cancellation);
                        continue;
                    }
                    target = pick.Address;
                }
                var name = Headsets.FirstOrDefault(h => h.Address == target)?.Name ?? target;
                _connectedAddress = target;
                ConnectStatus = $"Found {name}";
                ConnectDetail = "Connecting…";
                try
                {
                    var acceptChangedKey = string.Equals(_acceptChangedKeyAt, target, StringComparison.OrdinalIgnoreCase);
                    var connection = await _backend.ConnectAsync(target, name, key, _store, acceptChangedKey, cancellation);
                    _acceptChangedKeyAt = null;
                    try
                    {
                        await OnConnectedAsync(connection, target, name, cancellation);
                        return;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _connection?.Dispose();
                        _connection = null;
                        ConnectStatus = $"Could not connect to {name}";
                        ConnectDetail = "Error: " + exception.Message + "\nSetup will try again in a few seconds.";
                        preferred = target;
                        await Task.Delay(RetryPause, cancellation);
                        continue;
                    }
                }
                catch (ConnectException exception) when (exception.Failure == ConnectFailure.HostKeyChanged)
                {
                    ConnectStatus = $"{name} looks different than last time";
                    ConnectDetail = exception.Message;
                    ShowForget = true;
                    return;
                }
                catch (ConnectException exception)
                {
                    if (!await _backend.IsDevkitAsync(target, cancellation))
                    {
                        ConnectStatus = $"Cannot reach {name}";
                        ConnectDetail = "Error: " + exception.Message + "\nPlease make sure the headset is on, awake and in Developer Mode.";
                        preferred = null;
                        await Task.Delay(RetryPause, cancellation);
                        continue;
                    }
                    ConnectStatus = $"Found {name}: please enable pairing (see steps shown below)";
                    ConnectDetail = "Waiting for the headset…";
                    var progress = new Progress<PairingAttempt>(attempt => ConnectDetail = DescribePairing(attempt));
                    var result = await _backend.PairAsync(target, key.PublicKeyLine, progress, cancellation);
                    if (result.Outcome != PairingOutcome.Paired)
                    {
                        ConnectStatus = "The headset did not accept the pairing request.";
                        ConnectDetail = EndSentence(result.Message) + " Setup will try again in a few seconds.";
                        await Task.Delay(RetryPause, cancellation);
                    }
                    preferred = target;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ConnectStatus = "Setup ran into a problem";
            ConnectDetail = "Error: " + exception.Message + "\nPlease try again. If this keeps happening, try restarting the installer.";
            ShowRetry = true;
        }
        finally
        {
            ConnectBusy = false;
        }
    }

    public static string EndSentence(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length == 0 || trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?') ? trimmed : trimmed + ".";
    }

    private static string DescribePairing(PairingAttempt attempt) => attempt.Outcome switch
    {
        PairingOutcome.NotInPairingMode => "Please click \"Pair new host\" in the Steam Frame.",
        PairingOutcome.NoAnswer => "Please accept the pairing request in the headset (see step 3).",
        PairingOutcome.SteamNotRunning => "Waiting for Steam. Please make sure SteamVR is running in the headset.",
        PairingOutcome.Unreachable => "Lost connection to the headset. Please make sure it is on and not in sleep mode.",
        PairingOutcome.Paired => "Pairing successful.",
        _ => attempt.Message,
    };

    private void DescribeChoice(IReadOnlyList<HeadsetCandidate> found)
    {
        var open = found.Where(c => !_skipped.Contains(c.Address)).ToList();
        if (found.Count == 0)
        {
            ConnectStatus = "No Steam Frame found";
            ConnectDetail = "Please make sure the headset is connected to your local network and that you have done the setup steps below. Installation will continue automatically once your Steam Frame is connected.";
        }
        else if (open.Count == 0)
        {
            ConnectStatus = "No other headsets found";
            ConnectDetail = "Please make sure developer mode is enabled.";
        }
        else if (open.Count == 1)
        {
            ConnectStatus = $"Found {open[0].Name}, but it may not be a Steam Frame";
            ConnectDetail = "Please select your headset. If it does not show up, please make sure developer mode is enabled.";
        }
        else
        {
            ConnectStatus = "Several devices found";
            ConnectDetail = "Please choose your Steam Frame from the list below.";
        }
    }

    private void UpdateHeadsetList(IReadOnlyList<HeadsetCandidate> found)
    {
        if (Headsets.SequenceEqual(found))
        {
            return;
        }
        var selected = SelectedHeadset?.Address;
        _updatingHeadsetList = true;
        Headsets.Clear();
        foreach (var candidate in found)
        {
            Headsets.Add(candidate);
        }
        SelectedHeadset = Headsets.FirstOrDefault(h => h.Address == selected);
        _updatingHeadsetList = false;
    }

    private async Task OnConnectedAsync(IHeadsetSession connection, string address, string name, CancellationToken cancellation)
    {
        _connection?.Dispose();
        _connection = connection;
        ConnectDetail = "Connected. Performing setup checks…";
        _store?.Remember(address, name, connection.HostKey);
        await ProbeAsync(cancellation);
        Step = SetupStep.Ready;
    }

    private async Task ProbeAsync(CancellationToken cancellation)
    {
        if (_connection is null || Manifest is null)
        {
            return;
        }
        var device = await _connection.ProbeAsync(Manifest.Id, cancellation);
        if (device.HostName.Length > 0)
        {
            _store?.Remember(_connection.Address, device.HostName, _connection.HostKey);
        }
        HeadsetName = device.HostName.Length > 0 ? device.HostName : _connection.Address;
        var free = device.FreeMegabytes >= 0 ? $" · {device.FreeMegabytes / 1024.0:0.#} GB free" : "";
        HeadsetDetails = $"SteamOS {device.OsVersion} · {_connection.Address}{free}";
        if (!string.Equals(_probedAddress, _connection.Address, StringComparison.OrdinalIgnoreCase))
        {
            _probedAddress = _connection.Address;
            AutoUpdate = true;
        }
        ConfirmingUninstall = false;
        ConfirmingUnpair = false;
        PurgeData = false;
        ReadyNote = "";
        IsInstalled = device.InstalledVersion.Length > 0 && !device.ForeignFolder;
        _installedBefore = IsInstalled ? device.InstalledVersion : "";
        CanInstall = device.IsFrame && !device.ForeignFolder;
        if (!device.IsFrame)
        {
            InstalledText = "This device does not seem to be a Steam Frame.";
            ReadyNote = $"Detected SteamOS variant '{device.Variant}' on '{device.Architecture}'. {AppName} is only compatible with the Steam Frame.";
            PrimaryActionText = "Cannot install";
            return;
        }
        if (device.ForeignFolder)
        {
            InstalledText = $"{AppName} cannot be installed: {DeviceProbe.AppFolder(Manifest.Id)} already exists on the headset.";
            ReadyNote = "A non-FrameKit app with that id already exists. Please check and remove it manually first.";
            PrimaryActionText = "Cannot install";
            return;
        }
        if (InstallChecks.OsTooOld(Manifest, device))
        {
            CanInstall = false;
            InstalledText = $"{AppName} needs SteamOS {Manifest.MinOsVersion} or newer.";
            ReadyNote = "Please update SteamOS and try again.";
            PrimaryActionText = "Cannot install";
            return;
        }
        if (InstallChecks.TooLittleSpace(Manifest, device))
        {
            CanInstall = false;
            InstalledText = $"Not enough free space: {AppName} needs at least {InstallChecks.RequiredFreeMb(Manifest)} MB.";
            ReadyNote = "Check the available storage space on your Steam Frame.";
            PrimaryActionText = "Cannot install";
            return;
        }
        if (device.InstalledVersion.Length == 0)
        {
            InstalledText = $"{AppName} is ready to install.";
            PrimaryActionText = $"Install {AppName}";
            return;
        }
        var order = VersionOrder.Compare(device.InstalledVersion, Manifest.Version);
        if (order == 0)
        {
            InstalledText = $"{AppName} {device.InstalledVersion} is already installed.";
            PrimaryActionText = "Reinstall";
        }
        else if (order < 0)
        {
            InstalledText = $"{AppName} {device.InstalledVersion} is installed. Setup will update it.";
            PrimaryActionText = $"Update to {Manifest.Version}";
        }
        else
        {
            InstalledText = $"A newer version of {AppName} is already installed ({device.InstalledVersion}).";
            ReadyNote = "Installing will downgrade to the older version. Only continue if you know what you are doing.";
            PrimaryActionText = $"Downgrade to {Manifest.Version}";
        }
        if (device.AutoUpdate is { } auto)
        {
            AutoUpdate = auto;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_connection is null || _package is null)
        {
            return;
        }
        BeginWork($"Installing {AppName} {AppVersion}…", $"installing {AppName}");
        OperationResult result;
        try
        {
            result = await _connection.InstallAsync(_package, AutoUpdate, RestartSteamVRAfter, AppendLog, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = new OperationResult(false, exception.Message, false, Array.Empty<string>());
        }
        if (result.Ok)
        {
            ShowDone(true, $"{AppName} {AppVersion} has been installed", _branding.FoundIt, "");
            OfferSteamVRRestart = result.SteamVRRestartNeeded;
        }
        else if (result.End != OperationEnd.Reported)
        {
            ShowNoResult(result, $"installing {AppName}", $"to check whether {AppName} is installed");
        }
        else
        {
            ShowFailure("Installation failed", result, InstallState(result.State));
        }
    }

    private string InstallState(OutcomeState state) => state switch
    {
        OutcomeState.Unchanged => "No changes were made.",
        OutcomeState.Reverted when _installedBefore.Length > 0 => $"No changes were made to installed {AppName} {_installedBefore}.",
        OutcomeState.Reverted => "No changes were made to the currently installed version.",
        OutcomeState.Removed => $"{AppName} has been removed but failed to reinstall." + (HasDataDirs ? " Settings are still saved." : ""),
        OutcomeState.Partial => $"{AppName} reinstall failed, please try again.",
        _ => "An unexpected error occurred. Installation status unclear.",
    };

    private string RemoveState(OutcomeState state) => state switch
    {
        OutcomeState.Unchanged => "Cancelled removal.",
        OutcomeState.Partial => $"{AppName} may be only partly removed.",
        _ => "Failed to read removal state.",
    };

    private string ReportAdvice =>
        $"Please copy the details and report this to {(Manifest?.Publisher is { Length: > 0 } publisher ? publisher : $"the developers of {AppName}")}.";

    private void ShowFailure(string title, OperationResult result, string state)
    {
        var advice = result.Cause switch
        {
            OutcomeCause.Device => "Please fix and try again.",
            OutcomeCause.Busy => "Please let the other setup finish first and try again.",
            OutcomeCause.Interrupted => "Click Back to try again.",
            _ => ReportAdvice,
        };
        var reportable = result.Cause is OutcomeCause.Failed or OutcomeCause.Unknown;
        ShowDone(false, title, result.Message, $"{state} {advice}", offerReport: reportable);
    }

    [RelayCommand]
    private void AskUninstall()
    {
        PurgeData = false;
        ConfirmingUninstall = true;
    }

    [RelayCommand]
    private void CancelUninstall() => ConfirmingUninstall = false;

    [RelayCommand]
    private void AskUnpair()
    {
        ConfirmingUninstall = false;
        UnpairQuestion = $"Unpair this PC from {HeadsetName}?";
        ConfirmingUnpair = true;
    }

    [RelayCommand]
    private void CancelUnpair() => ConfirmingUnpair = false;

    [RelayCommand]
    private async Task UnpairAsync()
    {
        if (_connection is null || _key is null)
        {
            return;
        }
        var headset = HeadsetName;
        BeginWork("Unpairing this PC…", "unpairing this PC");
        UnpairResult result;
        try
        {
            result = await _connection.UnpairAsync(_key, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = new UnpairResult(false, 0, exception.Message);
        }
        if (!result.Ok)
        {
            ShowDone(false, "Unpairing failed", result.Message, ReportAdvice, offerReport: true);
            return;
        }
        _connection.Dispose();
        _connection = null;
        _connectedAddress = "";
        ShowDone(true, "Device unpaired",
            result.Removed > 0
                ? "Next setup will require to pair again."
                : $"{headset} was already not paired.",
            "");
        ShowDevModeNote = false;
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (_connection is null || Manifest is null)
        {
            return;
        }
        var purge = PurgeData;
        BeginWork($"Removing {AppName}…", $"removing {AppName}");
        OperationResult result;
        try
        {
            result = await _connection.UninstallAsync(Manifest.Id, purge, AppendLog, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = new OperationResult(false, exception.Message, false, Array.Empty<string>());
        }
        if (result.Ok)
        {
            ShowDone(true, $"{AppName} was removed",
                !purge && result.KeptData ? "Your settings have been kept." : "",
                "");
        }
        else if (result.End != OperationEnd.Reported)
        {
            ShowNoResult(result, $"removing {AppName}", $"to check whether {AppName} was removed");
        }
        else
        {
            ShowFailure("Removal failed", result, RemoveState(result.State));
        }
    }

    [RelayCommand]
    private async Task RestartSteamVRAsync()
    {
        if (_connection is null)
        {
            return;
        }
        OfferSteamVRRestart = false;
        OperationResult result;
        try
        {
            result = await _connection.RestartSteamVRAsync(AppendLog, CancellationToken.None);
        }
        catch (Exception exception)
        {
            AppendLog("Restarting SteamVR failed: " + exception.Message);
            result = new OperationResult(false, exception.Message, false, Array.Empty<string>());
        }
        DoneHint = result.Ok
            ? "SteamVR is restarting."
            : "SteamVR could not be restarted, please restart it manually.";
    }

    [RelayCommand]
    private async Task CopyDetailsAsync()
    {
        var text = new StringBuilder();
        text.AppendLine($"{AppName} {AppVersion} Setup").AppendLine(DoneTitle).AppendLine(DoneMessage).AppendLine();
        text.Append(_log);
        if (_connection is not null && Manifest is not null)
        {
            try
            {
                text.AppendLine().AppendLine("--- headset").AppendLine(await _connection.DiagnosticsAsync(Manifest, CancellationToken.None));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                text.AppendLine("(couldn't collect details from the headset: " + exception.Message + ")");
            }
        }
        var copy = CopyToClipboard ?? SystemClipboard();
        if (copy is null)
        {
            return;
        }
        try
        {
            await copy(text.ToString());
            CopyDetailsText = "Copied";
        }
        catch (Exception exception)
        {
            AppendLog("Copying the details failed: " + exception.Message);
            CopyDetailsText = "Could not copy to clipboard";
        }
    }

    private Func<string, Task>? SystemClipboard()
    {
        var clipboard = _view is null ? null : TopLevel.GetTopLevel(_view)?.Clipboard;
        return clipboard is null ? null : text => clipboard.SetTextAsync(text);
    }

    [RelayCommand]
    private async Task StartOverAsync()
    {
        if (_connection is null)
        {
            Step = SetupStep.Connect;
            StartConnecting(null);
            return;
        }
        try
        {
            await ProbeAsync(CancellationToken.None);
            Step = SetupStep.Ready;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _connection.Dispose();
            _connection = null;
            Step = SetupStep.Connect;
            StartConnecting(null);
        }
    }

    [RelayCommand]
    private void Close() => _view?.Close();

    private void BeginWork(string title, string workingOn)
    {
        _log.Clear();
        if (_store is { LoadProblem.Length: > 0 })
        {
            _log.AppendLine(_store.LoadProblem);
        }
        LogText = _log.ToString();
        WorkingTitle = title;
        _workingOn = workingOn;
        ConfirmingClose = false;
        ConfirmingUninstall = false;
        ConfirmingUnpair = false;
        Step = SetupStep.Working;
    }

    private void ShowNoResult(OperationResult result, string workingOn, string toCheck)
    {
        var message = result.End == OperationEnd.TimedOut
            ? $"Headset setup did not finish {workingOn} after {SetupOperations.OperationTimeout.TotalMinutes:0} minutes. Please check and try again."
            : $"Setup lost connection to the headset during {workingOn} ({result.Message.TrimEnd('.')}).";
        ShowDone(false, "No response from headset", message,
            $"The headset might still finish on its own. Click Back in a few minutes {toCheck}.");
    }

    private void ShowDone(bool success, string title, string message, string hint, bool offerReport = false)
    {
        ConfirmingClose = false;
        DoneSuccess = success;
        DoneTitle = title;
        DoneMessage = message;
        DoneHint = hint;
        ShowDevModeNote = success;
        OfferSteamVRRestart = false;
        ShowCopyDetails = !success;
        ShowReportLink = offerReport && SupportUri is not null;
        CopyDetailsText = "Copy details";
        Step = SetupStep.Done;
    }

    private void AppendLog(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _log.AppendLine(line);
            LogText = _log.ToString();
        });
    }
}

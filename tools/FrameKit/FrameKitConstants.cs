namespace FrameKit;

internal static class FrameKitConstants
{
    public const int Format = 1;

    public const string IdPattern = "^(?:[a-z0-9][a-z0-9-]{1,38}[a-z0-9])\\z";
    public const int IdMinLength = 3;
    public const int IdMaxLength = 40;
    public const string ReservedId = "framekit";
    public const string ReservedIdPrefix = "framekit-";
    public const string VersionPattern = "^(?:[0-9]+\\.[0-9]+\\.[0-9]+(-[0-9A-Za-z.]+)?)\\z";
    public const int MaxVersionLength = 40;
    public const int MaxDescriptionLength = 300;
    public const string OsVersionPattern = "^(?:[0-9]+(\\.[0-9]+)*)\\z";
    public const string DefaultMinOsVersion = "0.4.5";
    public const int DefaultFreeMb = 20;
    public const int SpaceMarginMb = 500;
    public const int HookTimeoutSeconds = 300;
    public static readonly string[] DataRoots = [".local/state", ".config", ".cache"];
    public const string ServiceSuffix = ".service";
    public const string TimerSuffix = ".timer";
    public const string RelativePathPattern = "^(?:[A-Za-z0-9._/-]+)\\z";
    public const int MaxRelativePathLength = 200;

    public const int MaxModuleBytes = 65536;
    public const int MaxIconBytes = 16384;
    public const int MaxActions = 4;
    public const int MaxToggles = 6;
    public static readonly string[] ButtonStyles = ["primary", "danger"];
    public const int MaxArguments = 32;
    public const string ElementIdPattern = "^(?:[A-Za-z0-9_-]{1,40})\\z";
    public const int MaxElementIdLength = 40;
    public const int MaxLabelBytes = 40;
    public const int MaxConfirmBytes = 120;
    public const int MinStatusInterval = 2;
    public const int MaxStatusInterval = 300;
    public const int DefaultStatusInterval = 5;
    public const int MaxStatusLines = 3;
    public const int MaxStatusLineBytes = 120;
    public const int StatusTimeoutSeconds = 10;
    public const int ActionTimeoutSeconds = 30;
    public const string AppToken = "%APP%";

    public const string ManifestJson = "manifest.json";
    public const string ManifestEnv = "manifest.env";
    public const string InstallScript = "install.sh";
    public const string UninstallScript = "uninstall.sh";
    public const string UpdateScript = "update.sh";
    public const string ConstantsScript = "constants.sh";
    public const string SigningKey = "signing-key.pem";
    public const string PanelVersionFile = "VERSION";
    public const string PanelSums = "SHA256SUMS";
    public const string PanelSignature = "SHA256SUMS.sig";
    public const string PanelKey = "framekit-panel-key.pem";
    public const string PackageFiles = "package-files.txt";
    public const string PackageSuffix = "-aarch64.tar.gz";

    public const string AppsRoot = ".local/share";
    public const string AppStateDir = "framekit";
    public const string CurrentLink = "current";
    public const string VersionsDir = "versions";
    public const string SharedDir = "framekit";
    public const string PanelModulesDir = "panel.d";
    public const string PanelHome = "panel";
    public const string UnitDir = ".config/systemd/user";
    public const string ReceiptFile = "receipt";
    public const string SettingsFile = "settings.env";
    public const string UpdateStateFile = "update.json";
    public const string UpdateLogFile = "update.log";
    public const string UpdateLockFile = "update.lock";
    public const string LockFile = "lock";
    public const string PreviousFile = "previous";
    public const string SteamvrRestartFile = "steamvr-restart";
    public const string SteamvrUnit = "steamvr.service";
    public const string PanelUnit = "framekit-panel.service";
    public const string PanelBinary = "framekit-panel";
    public const string AuthorizedKeys = ".ssh/authorized_keys";
    public const string PairingName = "FrameKit-Steam-Frame-Application-Setup";

    public const string ResultPrefix = "FK-RESULT";
    public const string ResultOk = "ok";
    public const string ResultError = "error";
    public const string SteamvrRestartNeeded = "FK-STEAMVR-RESTART-NEEDED";
    public const string OutcomePrefix = "FK-OUTCOME";
    public const string OutcomeUnchanged = "unchanged";
    public const string OutcomeReverted = "reverted";
    public const string OutcomeRemoved = "removed";
    public const string OutcomePartial = "partial";
    public const string CauseDevice = "device";
    public const string CauseBusy = "busy";
    public const string CauseInterrupted = "interrupted";
    public const string CauseFailed = "failed";
    public const string KeptDataLine = "Settings and data kept in:";
}

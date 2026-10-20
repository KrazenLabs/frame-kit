#pragma once

#include <cstddef>

namespace fkp::constants {

inline constexpr int kFormat = 1;

inline constexpr const char* kIdPattern = "[a-z0-9][a-z0-9-]{1,38}[a-z0-9]";
inline constexpr int kIdMinLength = 3;
inline constexpr int kIdMaxLength = 40;
inline constexpr const char* kReservedId = "framekit";
inline constexpr const char* kReservedIdPrefix = "framekit-";
inline constexpr const char* kVersionPattern = "[0-9]+\\.[0-9]+\\.[0-9]+(-[0-9A-Za-z.]+)?";
inline constexpr int kMaxVersionLength = 40;
inline constexpr int kMaxDescriptionLength = 300;
inline constexpr const char* kOsVersionPattern = "[0-9]+(\\.[0-9]+)*";
inline constexpr const char* kDefaultMinOsVersion = "0.4.5";
inline constexpr int kDefaultFreeMb = 20;
inline constexpr int kSpaceMarginMb = 500;
inline constexpr int kHookTimeoutSeconds = 300;
inline constexpr const char* kDataRoots[] = {".local/state", ".config", ".cache"};
inline constexpr std::size_t kDataRootsCount = 3;
inline constexpr const char* kServiceSuffix = ".service";
inline constexpr const char* kTimerSuffix = ".timer";
inline constexpr const char* kRelativePathPattern = "[A-Za-z0-9._/-]+";
inline constexpr int kMaxRelativePathLength = 200;

inline constexpr int kMaxModuleBytes = 65536;
inline constexpr int kMaxIconBytes = 16384;
inline constexpr int kMaxActions = 4;
inline constexpr int kMaxToggles = 6;
inline constexpr const char* kButtonStyles[] = {"primary", "danger"};
inline constexpr std::size_t kButtonStylesCount = 2;
inline constexpr int kMaxArguments = 32;
inline constexpr const char* kElementIdPattern = "[A-Za-z0-9_-]{1,40}";
inline constexpr int kMaxElementIdLength = 40;
inline constexpr int kMaxLabelBytes = 40;
inline constexpr int kMaxConfirmBytes = 120;
inline constexpr int kMinStatusInterval = 2;
inline constexpr int kMaxStatusInterval = 300;
inline constexpr int kDefaultStatusInterval = 5;
inline constexpr int kMaxStatusLines = 3;
inline constexpr int kMaxStatusLineBytes = 120;
inline constexpr int kStatusTimeoutSeconds = 10;
inline constexpr int kActionTimeoutSeconds = 30;
inline constexpr const char* kAppToken = "%APP%";

inline constexpr const char* kManifestJson = "manifest.json";
inline constexpr const char* kManifestEnv = "manifest.env";
inline constexpr const char* kInstallScript = "install.sh";
inline constexpr const char* kUninstallScript = "uninstall.sh";
inline constexpr const char* kUpdateScript = "update.sh";
inline constexpr const char* kConstantsScript = "constants.sh";
inline constexpr const char* kSigningKey = "signing-key.pem";
inline constexpr const char* kPanelVersionFile = "VERSION";
inline constexpr const char* kPanelNotices = "THIRD-PARTY-NOTICES.txt";
inline constexpr const char* kPanelSums = "SHA256SUMS";
inline constexpr const char* kPanelSignature = "SHA256SUMS.sig";
inline constexpr const char* kPanelKey = "framekit-panel-key.pem";
inline constexpr const char* kPackageFiles = "package-files.txt";
inline constexpr const char* kPackageSuffix = "-aarch64.tar.gz";

inline constexpr const char* kAppsRoot = ".local/share";
inline constexpr const char* kAppStateDir = "framekit";
inline constexpr const char* kCurrentLink = "current";
inline constexpr const char* kVersionsDir = "versions";
inline constexpr const char* kSharedDir = "framekit";
inline constexpr const char* kPanelModulesDir = "panel.d";
inline constexpr const char* kPanelHome = "panel";
inline constexpr const char* kUnitDir = ".config/systemd/user";
inline constexpr const char* kReceiptFile = "receipt";
inline constexpr const char* kSettingsFile = "settings.env";
inline constexpr const char* kUpdateStateFile = "update.json";
inline constexpr const char* kUpdateLogFile = "update.log";
inline constexpr const char* kUpdateLockFile = "update.lock";
inline constexpr const char* kLockFile = "lock";
inline constexpr const char* kPreviousFile = "previous";
inline constexpr const char* kSteamvrRestartFile = "steamvr-restart";
inline constexpr const char* kSteamvrUnit = "steamvr.service";
inline constexpr const char* kPanelUnit = "framekit-panel.service";
inline constexpr const char* kPanelBinary = "framekit-panel";
inline constexpr const char* kAuthorizedKeys = ".ssh/authorized_keys";
inline constexpr const char* kPairingName = "FrameKit-Steam-Frame-Application-Setup";

inline constexpr const char* kResultPrefix = "FK-RESULT";
inline constexpr const char* kResultOk = "ok";
inline constexpr const char* kResultError = "error";
inline constexpr const char* kSteamvrRestartNeeded = "FK-STEAMVR-RESTART-NEEDED";
inline constexpr const char* kOutcomePrefix = "FK-OUTCOME";
inline constexpr const char* kOutcomeUnchanged = "unchanged";
inline constexpr const char* kOutcomeReverted = "reverted";
inline constexpr const char* kOutcomeRemoved = "removed";
inline constexpr const char* kOutcomePartial = "partial";
inline constexpr const char* kCauseDevice = "device";
inline constexpr const char* kCauseBusy = "busy";
inline constexpr const char* kCauseInterrupted = "interrupted";
inline constexpr const char* kCauseFailed = "failed";
inline constexpr const char* kKeptDataLine = "Settings and data kept in:";

}

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FrameKit;

public sealed partial class FrameAppManifest
{
    public const int Format = FrameKitConstants.Format;
    public const int MaxPayloadNameBytes = 255;
    private const char ByteOrderMark = (char)0xFEFF;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private FrameAppManifest(string path, JsonObject json)
    {
        FilePath = path;
        Json = json;
    }

    public string FilePath { get; }
    public JsonObject Json { get; }
    public string Id => Text("id");
    public string Name => Text("name");
    public string Version => Text("version");
    public string Description => Text("description");
    public string Publisher => Text("publisher");
    public string SupportUrl => Text("support_url");
    public string MinOsVersion => Text("min_os_version");
    public long FreeMb => Json["free_mb"]!.GetValue<long>();
    public IReadOnlyList<string> Executables => Strings("executables");
    public bool Service => Flag("service");
    public string ServiceUnit => Id + FrameKitConstants.ServiceSuffix;
    public bool Timer => Flag("timer");
    public string TimerUnit => Id + FrameKitConstants.TimerSuffix;
    public IReadOnlyList<string> SteamVRDrivers => Strings("steamvr_drivers");
    public IReadOnlyList<string> DataDirs => Strings("data_dirs");
    public bool RestartSteamVR => Json["restart_steamvr"]!.GetValue<bool>();
    public string Panel => Text("panel");
    public string UpdateIndex => Json["updates"]?["index"]?.GetValue<string>() ?? "";
    public string UpdateBase => Json["updates"]?["base"]?.GetValue<string>() ?? "";
    public string SetupFoundIt => (Json["setup"] as JsonObject)?["found_it"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    public string SetupIcon => (Json["setup"] as JsonObject)?["icon"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    public string PackageFileName => $"{Id}-{Version}{FrameKitConstants.PackageSuffix}";
    public string IndexFileName => $"{Id}-latest.txt";

    [GeneratedRegex(FrameKitConstants.IdPattern)]
    private static partial Regex IdPattern();

    [GeneratedRegex(FrameKitConstants.VersionPattern)]
    public static partial Regex VersionPattern();

    [GeneratedRegex(FrameKitConstants.OsVersionPattern)]
    private static partial Regex OsVersionPattern();

    [GeneratedRegex(FrameKitConstants.RelativePathPattern)]
    private static partial Regex RelativePathPattern();

    [GeneratedRegex(FrameKitConstants.ElementIdPattern)]
    private static partial Regex ElementIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+\z")]
    private static partial Regex DataDirSegmentPattern();

    [GeneratedRegex(@"^https://[A-Za-z0-9._~:/?#\[\]@!$&()*+,;=%-]+\z")]
    private static partial Regex HttpsUrlPattern();

    [GeneratedRegex(@"^file:///[A-Za-z0-9._~/-]+\z")]
    private static partial Regex FileUrlPattern();

    [GeneratedRegex(@"^mailto:[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+\z")]
    private static partial Regex MailtoPattern();

    public static FrameAppManifest Load(string path, string? payload, bool allowFileUrls = false)
    {
        if (!File.Exists(path))
        {
            throw new KitError($"{path} not found");
        }
        var text = ReadUtf8Text(path) ?? throw new KitError($"{path} is not UTF-8");
        JsonObject json;
        try
        {
            json = JsonNode.Parse(text) as JsonObject ?? throw new KitError($"{path} must contain a JSON object");
        }
        catch (JsonException exception)
        {
            throw new KitError($"{path} is not valid JSON: {exception.Message}");
        }
        var manifest = new FrameAppManifest(Path.GetFullPath(path), json);
        manifest.Validate(payload, allowFileUrls);
        return manifest;
    }

    private static string? ReadUtf8Text(string path)
    {
        try
        {
            var text = StrictUtf8.GetString(File.ReadAllBytes(path));
            return text.StartsWith(ByteOrderMark) ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    public static void CheckRelativePath(string path, string what)
    {
        if (!RelativePathPattern().IsMatch(path) || path.StartsWith('/') || path.Split('/').Contains("..") ||
            path.Length > FrameKitConstants.MaxRelativePathLength)
        {
            throw new KitError($"{what}: '{path}' must be a relative path, {FrameKitConstants.MaxRelativePathLength} characters or less");
        }
    }

    public static void CheckPayloadPath(string path)
    {
        var shown = string.Concat(path.Select(c => char.IsControl(c) ? $"\\u{(int)c:x4}" : c.ToString()));
        var parts = path.Split('/');
        if (path.StartsWith('/') || parts.Any(part => part is "" or "." or ".."))
        {
            throw new KitError($"payload file '{shown}' must be a relative child path");
        }
        if (path.Any(c => char.IsControl(c) || c == '\\'))
        {
            throw new KitError($"payload file '{shown}': cannot contain control characters or '\\'");
        }
        if (path.Contains('\uFFFD') || !IsValidUtf8Text(path))
        {
            throw new KitError($"payload file '{shown}': no valid UTF-8");
        }
        var tooLong = parts.FirstOrDefault(part => Encoding.UTF8.GetByteCount(part) > MaxPayloadNameBytes);
        if (tooLong is not null)
        {
            throw new KitError($"payload file '{shown}': '{tooLong}' cannot be more than {MaxPayloadNameBytes} bytes");
        }
    }

    private static bool IsValidUtf8Text(string text)
    {
        try
        {
            StrictUtf8.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public string DeviceManifestJson(string panelVersion)
    {
        var device = new JsonObject { ["format"] = Format };
        foreach (var key in new[] { "id", "name", "version", "description", "publisher", "support_url", "min_os_version", "free_mb", "executables",
                                    "service", "timer", "steamvr_drivers", "restart_steamvr", "data_dirs", "panel", "updates" })
        {
            device[key] = Json[key]!.DeepClone();
        }
        device["panel_version"] = panelVersion;
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return device.ToJsonString(options).Replace("\r\n", "\n") + "\n";
    }

    public string ManifestEnv(string panelVersion)
    {
        var values = new (string Key, string Value)[]
        {
            ("FK_FORMAT", Format.ToString()),
            ("FK_ID", Id),
            ("FK_NAME", Name),
            ("FK_VERSION", Version),
            ("FK_PUBLISHER", Publisher),
            ("FK_MIN_OS", MinOsVersion),
            ("FK_FREE_MB", FreeMb.ToString()),
            ("FK_EXECUTABLES", string.Join(' ', Executables)),
            ("FK_SERVICE", Service ? "1" : "0"),
            ("FK_TIMER", Timer ? "1" : "0"),
            ("FK_DRIVERS", string.Join(' ', SteamVRDrivers)),
            ("FK_RESTART_STEAMVR", RestartSteamVR ? "1" : "0"),
            ("FK_DATA_DIRS", string.Join(' ', DataDirs)),
            ("FK_PANEL", Panel),
            ("FK_UPDATE_INDEX", UpdateIndex),
            ("FK_UPDATE_BASE", UpdateBase),
            ("FK_PANEL_VERSION", panelVersion),
        };
        var text = new StringBuilder();
        foreach (var (key, value) in values)
        {
            if (HasControlCharacter(value))
            {
                throw new KitError($"{key} must not contain line breaks or other control characters");
            }
            text.Append(key).Append("='").Append(value.Replace("'", "'\\''")).Append("'\n");
        }
        return text.ToString();
    }

    private void Validate(string? payload, bool allowFileUrls)
    {
        var problems = new List<string>();
        foreach (var key in new[] { "id", "name", "version", "description", "publisher" })
        {
            if (!IsString(key))
            {
                problems.Add($"'{key}' is missing or not a text");
            }
        }
        Fail(problems);

        foreach (var key in new[] { "name", "publisher" })
        {
            if (Text(key).Trim().Length == 0)
            {
                problems.Add($"'{key}' must not be empty");
            }
        }
        if (!IdPattern().IsMatch(Id))
        {
            problems.Add($"'id' must contain {FrameKitConstants.IdMinLength}-{FrameKitConstants.IdMaxLength} alphanumeric lower case characters or '-' " +
                         "(not starting or ending with '-')");
        }
        else if (Id == FrameKitConstants.ReservedId || Id.StartsWith(FrameKitConstants.ReservedIdPrefix, StringComparison.Ordinal))
        {
            problems.Add($"\"{FrameKitConstants.ReservedId}\" is a reserved id");
        }
        if (IsString("description") && Description.Length > FrameKitConstants.MaxDescriptionLength)
        {
            problems.Add($"'description' must be {FrameKitConstants.MaxDescriptionLength} characters or less");
        }
        if (!VersionPattern().IsMatch(Version))
        {
            problems.Add("'version' must be valid semver format");
        }
        else if (Version.Length > FrameKitConstants.MaxVersionLength)
        {
            problems.Add($"'version' must be {FrameKitConstants.MaxVersionLength} characters or less");
        }
        Json["min_os_version"] ??= FrameKitConstants.DefaultMinOsVersion;
        Json["free_mb"] ??= JsonValue.Create((long)FrameKitConstants.DefaultFreeMb);
        foreach (var key in new[] { "executables", "steamvr_drivers", "data_dirs" })
        {
            Json[key] ??= new JsonArray();
            if (Json[key] is not JsonArray array || array.Any(item => item is not JsonValue value || !value.TryGetValue<string>(out _)))
            {
                problems.Add($"'{key}' must be a list of texts");
            }
        }
        Json["service"] ??= false;
        Json["timer"] ??= false;
        Json["restart_steamvr"] ??= false;
        Json["panel"] ??= "";
        Json["updates"] ??= new JsonObject();
        Json["support_url"] ??= "";
        if (!IsString("support_url"))
        {
            problems.Add("'support_url' must be a text");
        }
        if (!IsString("min_os_version"))
        {
            problems.Add("'min_os_version' must be a version number");
        }
        if (!IsString("panel"))
        {
            problems.Add("'panel' must be a text");
        }
        if (Json["updates"] is not JsonObject)
        {
            problems.Add("'updates' must be an object with 'index' and 'base'");
        }
        Fail(problems);

        if (!OsVersionPattern().IsMatch(MinOsVersion))
        {
            problems.Add("'min_os_version' must be a version number");
        }
        if (Json["free_mb"] is not JsonValue freeMb || !freeMb.TryGetValue<long>(out var free) || free < 0)
        {
            problems.Add("'free_mb' must be a whole number");
        }
        if (Json["service"] is not JsonValue service || !service.TryGetValue<bool>(out _))
        {
            problems.Add("'service' must be true or false");
        }
        if (Json["timer"] is not JsonValue timer || !timer.TryGetValue<bool>(out _))
        {
            problems.Add("'timer' must be true or false");
        }
        else if (Timer && !Service)
        {
            problems.Add($"\"timer\" needs \"service\": true: the timer runs {ServiceUnit}");
        }
        if (Json["restart_steamvr"] is not JsonValue restart || !restart.TryGetValue<bool>(out _))
        {
            problems.Add("'restart_steamvr' must be true or false");
        }
        foreach (var dataDir in DataDirs)
        {
            if (!IsOwnDataDir(dataDir))
            {
                problems.Add($"data dir '{dataDir}' must be .local/state/{Id}, .config/{Id} or .cache/{Id}, or a child directory");
            }
        }
        if (payload is not null)
        {
            CheckPayload(payload, problems);
        }
        CheckUpdates(allowFileUrls, problems);
        if (SupportUrl.Length > 0 && !HttpsUrlPattern().IsMatch(SupportUrl) && !MailtoPattern().IsMatch(SupportUrl))
        {
            problems.Add("'support_url' must be an https URL or a mailto: address");
        }
        CheckSetupIcon(problems);
        foreach (var key in new[] { "name", "version", "publisher", "min_os_version" })
        {
            if (HasControlCharacter(Text(key)))
            {
                problems.Add($"'{key}' must not contain line breaks or other control characters");
            }
        }
        Fail(problems);
    }

    private void CheckPayload(string payload, List<string> problems)
    {
        foreach (var executable in Executables)
        {
            CheckRelativePath(executable, "executables");
            if (!File.Exists(Path.Combine(payload, executable)))
            {
                problems.Add($"executable '{executable}' is missing");
            }
        }
        var unitPath = Path.Combine(payload, ServiceUnit);
        if (Service && !File.Exists(unitPath))
        {
            problems.Add($"\"service\" is true, but payload contains no {ServiceUnit} definition");
        }
        else if (Service && !File.ReadAllText(unitPath).Contains(CurrentFolder, StringComparison.Ordinal))
        {
            problems.Add($"{ServiceUnit} should run from %h/{CurrentFolder}");
        }
        else if (!Service && File.Exists(unitPath))
        {
            problems.Add($"payload contains {ServiceUnit}, but \"service\" is set to false");
        }
        if (Directory.EnumerateFiles(payload, "*.service").Select(Path.GetFileName).Any(name => name != ServiceUnit))
        {
            problems.Add("you can only include one service definition");
        }
        var timerPath = Path.Combine(payload, TimerUnit);
        if (Timer && !File.Exists(timerPath))
        {
            problems.Add($"\"timer\" is true, but payload is missing {TimerUnit}");
        }
        else if (Timer && !File.ReadAllLines(timerPath).Any(line => line.Trim() == $"Unit={ServiceUnit}"))
        {
            problems.Add($"{TimerUnit} has no service bound");
        }
        else if (!Timer && File.Exists(timerPath))
        {
            problems.Add($"payload contains {TimerUnit}, but \"timer\" is set to false");
        }
        if (Directory.EnumerateFiles(payload, "*.timer").Select(Path.GetFileName).Any(name => name != TimerUnit))
        {
            problems.Add("only include one timer");
        }
        foreach (var driver in SteamVRDrivers)
        {
            CheckRelativePath(driver, "steamvr_drivers");
            if (!File.Exists(Path.Combine(payload, driver, "driver.vrdrivermanifest")))
            {
                problems.Add($"'{driver}' has no driver.vrdrivermanifest");
            }
        }
        if (Panel.Length > 0)
        {
            CheckRelativePath(Panel, "panel");
            var panelPath = Path.Combine(payload, Panel);
            if (!File.Exists(panelPath))
            {
                problems.Add($"payload is missing panel module '{Panel}'");
            }
            else
            {
                problems.AddRange(CheckPanelModule(panelPath, payload));
            }
        }
    }

    public string SetupIconPath => SetupIcon.Length > 0 ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(FilePath)!, SetupIcon)) : "";

    private void CheckSetupIcon(List<string> problems)
    {
        if (Json["setup"] is { } setup && setup is not JsonObject)
        {
            problems.Add("setup must be an object with found_it and icon");
            return;
        }
        if ((Json["setup"] as JsonObject)?["icon"] is { } icon && (icon is not JsonValue value || !value.TryGetValue<string>(out _)))
        {
            problems.Add("setup.icon must be a relative path to a 256 × 256 PNG");
            return;
        }
        if (SetupIcon.Length > 0 && PngCheck.SetupIconProblem(SetupIconPath) is { } problem)
        {
            problems.Add(problem);
        }
    }

    private void CheckUpdates(bool allowFileUrls, List<string> problems)
    {
        var updates = (JsonObject)Json["updates"]!;
        var typesOk = true;
        foreach (var key in new[] { "index", "base" })
        {
            if (updates[key] is null)
            {
                continue;
            }
            if (updates[key] is not JsonValue value || !value.TryGetValue<string>(out var url))
            {
                problems.Add($"updates.{key} must be a text");
                typesOk = false;
                continue;
            }
            if (url.Length > 0 && !(HttpsUrlPattern().IsMatch(url) || (allowFileUrls && FileUrlPattern().IsMatch(url))))
            {
                problems.Add($"updates.{key} must be a https:// URL");
            }
        }
        if (!typesOk)
        {
            return;
        }
        if ((UpdateIndex.Length > 0) != (UpdateBase.Length > 0))
        {
            problems.Add("updates need either both 'index' and 'base', or neither");
        }
        if (UpdateBase.Length > 0 && !UpdateBase.EndsWith('/'))
        {
            problems.Add("updates.base must end with '/'");
        }
    }

    private static List<string> CheckPanelModule(string path, string payload)
    {
        var problems = new List<string>();
        if (new FileInfo(path).Length > FrameKitConstants.MaxModuleBytes)
        {
            return [$"panel module cannot be larger than {FrameKitConstants.MaxModuleBytes / 1024} KB"];
        }
        if (ReadUtf8Text(path) is not { } text)
        {
            return ["panel module is not UTF-8"];
        }
        JsonObject panel;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject parsed)
            {
                return ["panel module must be a JSON object"];
            }
            panel = parsed;
        }
        catch (JsonException exception)
        {
            return [$"panel module is not valid JSON: {exception.Message}"];
        }

        void Command(JsonNode? value, string what)
        {
            if (value is not JsonArray array || array.Count == 0 || array.Count > FrameKitConstants.MaxArguments ||
                array.Any(item => item is not JsonValue v || !v.TryGetValue<string>(out var text) || text.Length == 0))
            {
                problems.Add($"panel {what} must be a list of 1-{FrameKitConstants.MaxArguments} non-empty texts");
            }
        }

        if (panel.ContainsKey("icon"))
        {
            if (panel["icon"] is not JsonValue iconValue || !iconValue.TryGetValue<string>(out var icon) || icon.Length == 0)
            {
                problems.Add("panel icon must be a SVG file");
            }
            else
            {
                CheckRelativePath(icon, "panel icon");
                var iconPath = Path.Combine(payload, icon);
                if (!File.Exists(iconPath))
                {
                    problems.Add($"panel icon '{icon}' missing from payload");
                }
                else if (new FileInfo(iconPath).Length > FrameKitConstants.MaxIconBytes)
                {
                    problems.Add($"panel icon '{icon}' cannot be larger than {FrameKitConstants.MaxIconBytes / 1024} KB");
                }
            }
        }
        if (panel.ContainsKey("status"))
        {
            var status = panel["status"] as JsonObject;
            Command(status?["command"], "status.command");
            var interval = (double)FrameKitConstants.DefaultStatusInterval;
            if (status is not null && status.ContainsKey("interval") &&
                !(status["interval"] is JsonValue intervalValue && intervalValue.TryGetValue(out interval)))
            {
                interval = -1;
            }
            if (interval < FrameKitConstants.MinStatusInterval || interval > FrameKitConstants.MaxStatusInterval)
            {
                problems.Add($"panel status.interval must be between {FrameKitConstants.MinStatusInterval}-{FrameKitConstants.MaxStatusInterval} seconds");
            }
        }
        CheckItems(panel, "actions", "action", FrameKitConstants.MaxActions, problems, (item, id) =>
        {
            Command(item["command"], $"action '{id}' command");
            if (item.ContainsKey("confirm") &&
                (item["confirm"] is not JsonValue confirmValue || !confirmValue.TryGetValue<string>(out var question) || question.Length == 0 ||
                 Encoding.UTF8.GetByteCount(question) > FrameKitConstants.MaxConfirmBytes))
            {
                problems.Add($"panel action '{id}' confirm question too long (max {FrameKitConstants.MaxConfirmBytes} bytes)");
            }
            if (item.ContainsKey("style") &&
                !(item["style"] is JsonValue styleValue && styleValue.TryGetValue<string>(out var style) && FrameKitConstants.ButtonStyles.Contains(style)))
            {
                problems.Add($"panel action '{id}' style must be {string.Join(" or ", FrameKitConstants.ButtonStyles.Select(s => $"\"{s}\""))}");
            }
            if (item.ContainsKey("wide") && !(item["wide"] is JsonValue wideValue && wideValue.TryGetValue<bool>(out _)))
            {
                problems.Add($"panel action '{id}' wide must be true or false");
            }
        });
        CheckItems(panel, "toggles", "toggle", FrameKitConstants.MaxToggles, problems, (item, id) =>
        {
            foreach (var key in new[] { "get", "on", "off" })
            {
                Command(item[key], $"toggle '{id}' {key}");
            }
        });
        return problems;
    }

    private static void CheckItems(JsonObject panel, string key, string singular, int limit, List<string> problems,
                                   Action<JsonObject, string> checkItem)
    {
        if (!panel.ContainsKey(key))
        {
            return;
        }
        if (panel[key] is not JsonArray items || items.Count > limit)
        {
            problems.Add($"too many panel {key} (max {limit})");
            return;
        }
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not JsonObject item ||
                item["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id) ||
                item["label"] is not JsonValue labelValue || !labelValue.TryGetValue<string>(out var label))
            {
                problems.Add($"panel {singular} {i} needs 'id' and 'label'");
                continue;
            }
            if (!ElementIdPattern().IsMatch(id))
            {
                problems.Add($"panel {singular} id '{id}' must be {FrameKitConstants.MaxElementIdLength} alphanumeric characters " +
                             "(plus '_' and '-') or less");
            }
            if (label.Length == 0 || Encoding.UTF8.GetByteCount(label) > FrameKitConstants.MaxLabelBytes)
            {
                problems.Add($"panel {singular} '{id}' needs a label of max {FrameKitConstants.MaxLabelBytes} bytes");
            }
            checkItem(item, id);
        }
    }

    private bool IsOwnDataDir(string path)
    {
        foreach (var root in FrameKitConstants.DataRoots)
        {
            var own = $"{root}/{Id}";
            if (path == own)
            {
                return true;
            }
            if (path.StartsWith(own + "/", StringComparison.Ordinal))
            {
                return path[(own.Length + 1)..].Split('/').All(part => DataDirSegmentPattern().IsMatch(part) && part != "." && part != "..");
            }
        }
        return false;
    }

    private string CurrentFolder => $"{FrameKitConstants.AppsRoot}/{Id}/{FrameKitConstants.CurrentLink}/";

    private bool IsString(string key) => Json[key] is JsonValue value && value.TryGetValue<string>(out _);

    private bool Flag(string key) => Json[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private string Text(string key) => Json[key]!.GetValue<string>();

    private IReadOnlyList<string> Strings(string key) => Json[key]!.AsArray().Select(item => item!.GetValue<string>()).ToList();

    private static bool HasControlCharacter(string value) => value.Any(char.IsControl);

    private static void Fail(List<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new KitError(string.Join("; ", problems));
        }
    }
}

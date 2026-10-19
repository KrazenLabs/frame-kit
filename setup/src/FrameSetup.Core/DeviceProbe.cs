using System.Text;

namespace FrameKit.Setup;

public sealed record DeviceInfo(
    string Variant,
    string OsVersion,
    string Architecture,
    string HostName,
    int FreeMegabytes,
    string InstalledVersion,
    bool? AutoUpdate,
    bool ForeignFolder)
{
    public bool IsFrame => Variant == "vr" && Architecture == "aarch64";
}

public static class DeviceProbe
{
    public const string Script = $$"""
        . /etc/os-release 2>/dev/null
        echo "variant=${VARIANT_ID:-}"
        echo "os=${VERSION_ID:-}"
        echo "arch=$(uname -m)"
        echo "host=$(uname -n)"
        echo "free_mb=$(df -Pk "$HOME" | awk 'NR == 2 { print int($4 / 1024) }')"
        A="$HOME/{{FrameKitConstants.AppsRoot}}/$1"
        if [ -f "$A/{{FrameKitConstants.CurrentLink}}/{{FrameKitConstants.ManifestEnv}}" ]; then
            echo "installed=$(sed -n "s/^FK_VERSION='\(.*\)'$/\1/p" "$A/{{FrameKitConstants.CurrentLink}}/{{FrameKitConstants.ManifestEnv}}")"
        fi
        if [ -f "$A/{{FrameKitConstants.AppStateDir}}/{{FrameKitConstants.SettingsFile}}" ]; then
            echo "auto=$(sed -n 's/^AUTO_UPDATE=\([01]\)$/\1/p' "$A/{{FrameKitConstants.AppStateDir}}/{{FrameKitConstants.SettingsFile}}")"
        fi
        if [ -L "$A" ] || { [ -e "$A" ] && [ ! -d "$A/{{FrameKitConstants.AppStateDir}}" ]; }; then echo "foreign=1"; fi
        """;

    public static string Command(string appId) => RemoteShell.ScriptCommand(Script, appId);

    public static string AppFolder(string appId) => $"~/{FrameKitConstants.AppsRoot}/{appId}";

    public static async Task<DeviceInfo> RunAsync(FrameConnection connection, string appId, CancellationToken cancellation)
    {
        var result = await connection.RunAsync(Command(appId), null, cancellation).ConfigureAwait(false);
        return Parse(result.Lines);
    }

    public static DeviceInfo Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var equals = line.IndexOf('=');
            if (equals > 0)
            {
                values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
        }
        string Get(string name) => values.TryGetValue(name, out var value) ? value : "";
        return new DeviceInfo(
            Get("variant"),
            Get("os"),
            Get("arch"),
            Get("host"),
            int.TryParse(Get("free_mb"), out var free) ? free : -1,
            Get("installed"),
            Get("auto") switch { "1" => true, "0" => false, _ => null },
            Get("foreign") == "1");
    }
}

public static class RemoteShell
{
    public const string EnvOverrideVariable = "FRAMEKIT_SETUP_REMOTE_ENV";

    public static string EnvPrefix
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable(EnvOverrideVariable);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "";
            }
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!parts.All(IsSafeAssignment))
            {
                throw new InvalidOperationException($"{EnvOverrideVariable} must be NAME=value pairs of [A-Za-z0-9_./:-].");
            }
            return string.Join(' ', parts) + " ";
        }
    }

    public static bool IsSafeAssignment(string assignment)
    {
        var equals = assignment.IndexOf('=');
        if (equals <= 0)
        {
            return false;
        }
        var name = assignment[..equals];
        var value = assignment[(equals + 1)..];
        return name.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_') && !char.IsDigit(name[0]) &&
               value.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or '/' or ':' or '-');
    }

    public static string Unattended(string command) => "{ " + command + "\n} 2>&1 | tee -p /dev/null";

    public static string ScriptCommand(string script, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/')))
            {
                throw new ArgumentException($"Unsafe argument '{argument}'.", nameof(arguments));
            }
        }
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(script.Replace("\r\n", "\n")));
        return $"echo {encoded} | base64 -d | {EnvPrefix}sh -s -- {string.Join(' ', arguments)}".TrimEnd();
    }
}

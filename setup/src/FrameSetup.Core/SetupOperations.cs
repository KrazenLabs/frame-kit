using System.Security.Cryptography;

namespace FrameKit.Setup;

public enum OperationEnd
{
    Reported,
    TimedOut,
    ConnectionLost,
}

public enum OutcomeState
{
    Unknown,
    Unchanged,
    Reverted,
    Removed,
    Partial,
}

public enum OutcomeCause
{
    Unknown,
    Device,
    Busy,
    Interrupted,
    Failed,
}

public sealed record OperationResult(bool Ok, string Message, bool SteamVRRestartNeeded, IReadOnlyList<string> Log,
    OperationEnd End = OperationEnd.Reported, OutcomeState State = OutcomeState.Unknown, OutcomeCause Cause = OutcomeCause.Unknown)
{
    public bool KeptData => Log.Any(line => line.StartsWith(FrameKitConstants.KeptDataLine, StringComparison.Ordinal));
}

public sealed record UnpairResult(bool Ok, int Removed, string Message);

public static class SetupOperations
{
    public const string RestartNeededMarker = FrameKitConstants.SteamvrRestartNeeded;
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(20);
    private const string UploadName = "framekit-setup-";
    private const string UploadPrefix = "/tmp/" + UploadName;
    private const string UnpackFolder = "package";
    private const string RemoveOldUploads =
        $"find /tmp -maxdepth 1 -type d -name '{UploadName}*' -user \"$(id -u)\" -mmin +60 -exec rm -rf {{}} + 2>/dev/null";
    private const string ResultPrefix = FrameKitConstants.ResultPrefix + " ";
    private const string ResultOk = FrameKitConstants.ResultOk;
    private const string ResultError = FrameKitConstants.ResultError + " ";
    private const string OutcomePrefix = FrameKitConstants.OutcomePrefix + " ";
    private const string RemovedPrefix = "removed ";
    private const string InstalledUninstall =
        $"$HOME/{FrameKitConstants.AppsRoot}/$1/{FrameKitConstants.CurrentLink}/{FrameKitConstants.UninstallScript}";

    public static async Task<OperationResult> InstallAsync(FrameConnection connection, AppPackage package, bool autoUpdate,
        bool restartSteamVR, Action<string>? onLine, CancellationToken cancellation)
    {
        var folder = UploadPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        try
        {
            var prepare = await connection.RunAsync($"{RemoveOldUploads}; mkdir -m 700 {folder}", onLine, cancellation).ConfigureAwait(false);
            if (prepare.ExitCode != 0)
            {
                return new OperationResult(false, "Failed to create temp directory on the headset.", false, prepare.Lines,
                    State: OutcomeState.Unchanged, Cause: OutcomeCause.Failed);
            }
            await connection.UploadAsync(package.Bytes, $"{folder}/{package.FileName}", cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RemoveUploadAsync(connection, folder).ConfigureAwait(false);
            return new OperationResult(false, $"Failed to copy {package.Manifest.Name} to the headset: {exception.Message}", false,
                Array.Empty<string>(), State: OutcomeState.Unchanged, Cause: OutcomeCause.Interrupted);
        }
        var command = InstallCommand(folder, package.FileName, autoUpdate, restartSteamVR);
        return await RunUnattendedAsync(connection, command, "Installation failed due to an internal error.", onLine, cancellation).ConfigureAwait(false);
    }

    public static string InstallCommand(string folder, string packageFile, bool autoUpdate, bool restartSteamVR)
    {
        var options = $"--auto-update {(autoUpdate ? 1 : 0)}" + (restartSteamVR ? " --restart-steamvr" : "");
        return $"cd {folder} && mkdir {UnpackFolder} && tar -xzf {packageFile} -C {UnpackFolder} && rm -f {packageFile} && " +
               $"cd {UnpackFolder} && {RemoteShell.EnvPrefix}sh ./{FrameKitConstants.InstallScript} {options}; " +
               $"status=$?; cd /; rm -rf {folder}; exit $status";
    }

    private static async Task RemoveUploadAsync(FrameConnection connection, string folder)
    {
        try
        {
            await connection.RunAsync($"rm -rf {folder}", null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    public static async Task<OperationResult> UninstallAsync(FrameConnection connection, string appId, bool purge,
        Action<string>? onLine, CancellationToken cancellation)
    {
        const string script = $$"""
            if [ ! -f "{{InstalledUninstall}}" ]; then
                echo "{{OutcomePrefix}}{{FrameKitConstants.OutcomeUnchanged}} {{FrameKitConstants.CauseFailed}}"
                echo "{{ResultPrefix}}{{ResultError}}app is not installed (could not find {{InstalledUninstall}})"
                exit 1
            fi
            sh "{{InstalledUninstall}}" $2
            """;
        var command = RemoteShell.ScriptCommand(script, appId, purge ? "--purge" : "");
        return await RunUnattendedAsync(connection, command, "Removal failed due to an internal error.", onLine, cancellation).ConfigureAwait(false);
    }

    private static async Task<OperationResult> RunUnattendedAsync(FrameConnection connection, string command, string fallback,
        Action<string>? onLine, CancellationToken cancellation)
    {
        CommandResult result;
        try
        {
            result = await connection.RunAsync(RemoteShell.Unattended(command), onLine, cancellation, OperationTimeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new OperationResult(false, exception.Message, false, Array.Empty<string>(), OperationEnd.ConnectionLost);
        }
        return Interpret(result, fallback);
    }

    public static async Task<UnpairResult> UnpairAsync(FrameConnection connection, SshKey key, CancellationToken cancellation)
    {
        var result = await connection.RunAsync(RemoteShell.ScriptCommand(UnpairScript(key)), null, cancellation).ConfigureAwait(false);
        var removed = result.Lines.LastOrDefault(l => l.StartsWith(RemovedPrefix, StringComparison.Ordinal));
        if (result.ExitCode == 0 && removed is not null && int.TryParse(removed[RemovedPrefix.Length..], out var count))
        {
            return new UnpairResult(true, count, "");
        }
        var tail = result.Lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        return new UnpairResult(false, 0, StartSentence(tail ?? "The headset didn't answer."));
    }

    public static string UnpairScript(SshKey key)
    {
        var blob = key.PublicKeyLine.Split(' ')[1];
        var names = FrameKitConstants.PairingName;
        return $$"""
            KEY='{{blob}}'
            NAMES='{{names}}'
            f="$HOME/{{FrameKitConstants.AuthorizedKeys}}"
            if [ ! -f "$f" ]; then echo "{{RemovedPrefix}}0"; exit 0; fi
            pairing() {
                awk -v key="$KEY" -v names="$NAMES" -v mode="$1" '
                    BEGIN { n = split(names, list, " "); for (i = 1; i <= n; i++) known[list[i]] = 1 }
                    { ours = ($1 == "ssh-rsa" && $2 == key && ($3 in known)) }
                    mode == "count" { if (ours) c++; next }
                    !ours { print }
                    END { if (mode == "count") print c + 0 }' "$f"
            }
            n=$(pairing count)
            if [ "$n" -gt 0 ]; then
                t="$f.framekit.$$"
                if ! { pairing keep >"$t" && chmod 600 "$t" && mv -f "$t" "$f"; }; then
                    rm -f "$t"
                    echo "could not change $f"
                    exit 1
                fi
            fi
            echo "{{RemovedPrefix}}$n"
            """;
    }

    public static async Task<OperationResult> RestartSteamVRAsync(FrameConnection connection, Action<string>? onLine, CancellationToken cancellation)
    {
        var result = await connection.RunAsync($"systemctl --user restart {FrameKitConstants.SteamvrUnit}", onLine, cancellation).ConfigureAwait(false);
        return new OperationResult(result.ExitCode == 0,
            result.ExitCode == 0 ? "SteamVR is restarting." : "SteamVR didn't restart.", false, result.Lines);
    }

    public static async Task<string> DiagnosticsAsync(FrameConnection connection, AppManifest manifest, CancellationToken cancellation)
    {
        const string script = $$"""
            . /etc/os-release 2>/dev/null
            echo "SteamOS $VERSION_ID ($VARIANT_ID, build $BUILD_ID), $(uname -m), kernel $(uname -r)"
            df -h "$HOME" | tail -n 1
            A="$HOME/{{FrameKitConstants.AppsRoot}}/$1"
            echo "--- $1"
            ls -la "$A" "$A/{{FrameKitConstants.VersionsDir}}" 2>&1
            S="$A/{{FrameKitConstants.AppStateDir}}"
            cat "$S/{{FrameKitConstants.ReceiptFile}}" "$S/{{FrameKitConstants.SettingsFile}}" "$S/{{FrameKitConstants.UpdateStateFile}}" 2>&1
            tail -n 30 "$S/{{FrameKitConstants.UpdateLogFile}}" 2>/dev/null
            echo "--- panel"
            "$HOME/{{FrameKitConstants.AppsRoot}}/{{FrameKitConstants.SharedDir}}/{{FrameKitConstants.PanelHome}}/{{FrameKitConstants.CurrentLink}}/{{FrameKitConstants.PanelBinary}}" status 2>&1
            echo "--- units"
            for unit in $2 $3 {{FrameKitConstants.PanelUnit}}; do systemctl --user --no-pager status "$unit" 2>&1 | head -n 5; done
            echo "--- journal"
            for unit in $2 $3 {{FrameKitConstants.PanelUnit}}; do journalctl --user -u "$unit" -n 60 --no-pager 2>&1; done
            """;
        var unit = manifest.Service ? manifest.Id + FrameKitConstants.ServiceSuffix : "";
        var timer = manifest.Timer ? manifest.Id + FrameKitConstants.TimerSuffix : "";
        var command = RemoteShell.ScriptCommand(script, manifest.Id, unit, timer);
        var result = await connection.RunAsync(command, null, cancellation).ConfigureAwait(false);
        return string.Join('\n', result.Lines);
    }

    public static OperationResult Interpret(CommandResult result, string fallback)
    {
        var restart = result.Lines.Any(l => l.Trim() == RestartNeededMarker);
        var last = result.Lines.LastOrDefault(l => l.StartsWith(ResultPrefix, StringComparison.Ordinal));
        if (last is null && result.TimedOut)
        {
            return new OperationResult(false, "", restart, result.Lines, OperationEnd.TimedOut);
        }
        if (last is null)
        {
            var tail = result.Lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
            return new OperationResult(false, tail is null ? fallback : $"{fallback} ({tail.Trim()})", restart, result.Lines);
        }
        var rest = last[ResultPrefix.Length..];
        if (rest.StartsWith(ResultOk, StringComparison.Ordinal))
        {
            return new OperationResult(true, rest.Length > ResultOk.Length + 1 ? rest[(ResultOk.Length + 1)..] : "", restart, result.Lines);
        }
        var message = rest.StartsWith(ResultError, StringComparison.Ordinal) ? rest[ResultError.Length..].Trim() : rest.Trim();
        if (message.Length == 0)
        {
            message = fallback;
        }
        var (state, cause) = Outcome(result.Lines, result.Lines.ToList().LastIndexOf(last));
        return new OperationResult(false, StartSentence(message), restart, result.Lines, State: state, Cause: cause);
    }

    private static (OutcomeState State, OutcomeCause Cause) Outcome(IReadOnlyList<string> lines, int resultIndex)
    {
        var line = lines.Take(resultIndex).LastOrDefault(l => l.StartsWith(OutcomePrefix, StringComparison.Ordinal));
        var words = line is null ? [] : line[OutcomePrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var state = words.ElementAtOrDefault(0) switch
        {
            FrameKitConstants.OutcomeUnchanged => OutcomeState.Unchanged,
            FrameKitConstants.OutcomeReverted => OutcomeState.Reverted,
            FrameKitConstants.OutcomeRemoved => OutcomeState.Removed,
            FrameKitConstants.OutcomePartial => OutcomeState.Partial,
            _ => OutcomeState.Unknown,
        };
        var cause = words.ElementAtOrDefault(1) switch
        {
            FrameKitConstants.CauseDevice => OutcomeCause.Device,
            FrameKitConstants.CauseBusy => OutcomeCause.Busy,
            FrameKitConstants.CauseInterrupted => OutcomeCause.Interrupted,
            FrameKitConstants.CauseFailed => OutcomeCause.Failed,
            _ => OutcomeCause.Unknown,
        };
        return (state, cause);
    }

    public static string StartSentence(string message)
    {
        var firstWord = message.Split(' ', 2)[0].TrimEnd('.', ',', ';', ':');
        if (message.Length == 0 || !char.IsAsciiLetterLower(message[0]) || firstWord.Contains('.') || firstWord.Contains('/'))
        {
            return message;
        }
        return char.ToUpperInvariant(message[0]) + message[1..];
    }
}

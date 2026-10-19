using System.Net;
using System.Text;
using System.Text.Json;

namespace FrameKit.Setup;

public enum PairingOutcome
{
    Paired,
    NotInPairingMode,
    NoAnswer,
    SteamNotRunning,
    Refused,
    Unreachable,
}

public sealed record PairingAttempt(PairingOutcome Outcome, string Message);

public static class Pairing
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    public static async Task<PairingAttempt> RegisterOnceAsync(string address, string publicKeyLine, CancellationToken cancellation)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var content = new StringContent(publicKeyLine.TrimEnd() + "\n", Encoding.ASCII, "text/plain");
            var uri = new Uri($"http://{Discovery.FormatHost(address)}:{Discovery.DevkitPort}/register");
            using var response = await http.PostAsync(uri, content, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Classify(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new PairingAttempt(PairingOutcome.NoAnswer, "The headset did not respond in time.");
        }
        catch (HttpRequestException exception)
        {
            return new PairingAttempt(PairingOutcome.Unreachable, exception.Message);
        }
    }

    public static PairingAttempt Classify(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.OK)
        {
            return new PairingAttempt(PairingOutcome.Paired, "");
        }
        var error = ErrorText(body);
        var lower = error.ToLowerInvariant();
        if (lower.Contains("pairing mode"))
        {
            return new PairingAttempt(PairingOutcome.NotInPairingMode, error);
        }
        if (lower.Contains("timeout") || lower.Contains("did not respond"))
        {
            return new PairingAttempt(PairingOutcome.NoAnswer, error);
        }
        if (lower.Contains("steam is not running"))
        {
            return new PairingAttempt(PairingOutcome.SteamNotRunning, error);
        }
        return new PairingAttempt(PairingOutcome.Refused, string.IsNullOrWhiteSpace(error) ? $"HTTP {(int)status}" : error);
    }

    public static async Task<PairingAttempt> PairAsync(string address, string publicKeyLine, IProgress<PairingAttempt>? progress, CancellationToken cancellation)
    {
        while (true)
        {
            var attempt = await RegisterOnceAsync(address, publicKeyLine, cancellation).ConfigureAwait(false);
            progress?.Report(attempt);
            switch (attempt.Outcome)
            {
                case PairingOutcome.Paired:
                case PairingOutcome.Refused:
                    return attempt;
                default:
                    await Task.Delay(RetryDelay, cancellation).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static string ErrorText(string body)
    {
        var trimmed = body.Trim();
        var start = trimmed.LastIndexOf('{');
        if (start >= 0)
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed[start..]);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
            }
        }
        return trimmed.Length > 200 ? trimmed[..200] : trimmed;
    }
}

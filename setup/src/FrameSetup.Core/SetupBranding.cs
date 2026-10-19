using System.Reflection;
using System.Text.Json;

namespace FrameKit.Setup;

public sealed record SetupBranding(string FoundIt, byte[]? Icon)
{
    public const string BrandingResource = "app-setup.json";
    public const string IconResource = "app-icon.png";

    public static SetupBranding Default { get; } = new("Installed applications are listed in a new Community Apps tab visible when clicking the battery icon.", null);

    public static SetupBranding FromAssembly(Assembly assembly)
    {
        var foundIt = Default.FoundIt;
        using (var stream = assembly.GetManifestResourceStream(BrandingResource))
        {
            if (stream is not null)
            {
                foundIt = Parse(stream) ?? foundIt;
            }
        }
        byte[]? icon = null;
        using (var stream = assembly.GetManifestResourceStream(IconResource))
        {
            if (stream is not null)
            {
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                icon = copy.ToArray();
            }
        }
        return new SetupBranding(foundIt, icon);
    }

    public static string? Parse(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
               document.RootElement.TryGetProperty("found_it", out var value) &&
               value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
    }
}

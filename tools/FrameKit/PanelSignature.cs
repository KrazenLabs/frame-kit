using System.Security.Cryptography;
using System.Text;

namespace FrameKit;

public static class PanelSignature
{
    public static readonly string[] SignedFiles =
        [FrameKitConstants.PanelBinary, FrameKitConstants.PanelUnit, FrameKitConstants.PanelVersionFile, FrameKitConstants.PanelNotices];
    private static readonly string[] TextFiles = [FrameKitConstants.PanelUnit, FrameKitConstants.PanelVersionFile, FrameKitConstants.PanelNotices];

    public static string Sums(string panelDir)
    {
        var text = new StringBuilder();
        foreach (var name in SignedFiles)
        {
            var path = Path.Combine(panelDir, name);
            if (!File.Exists(path))
            {
                throw new KitError($"the panel in {panelDir} is missing {name}");
            }
            var data = File.ReadAllBytes(path);
            if (TextFiles.Contains(name) && data.Contains((byte)'\r'))
            {
                throw new KitError($"{path} has incorrect line endings");
            }
            text.Append(Convert.ToHexStringLower(SHA256.HashData(data))).Append("  ").Append(name).Append('\n');
        }
        return text.ToString();
    }

    public static string Sign(string panelDir, string keyPath, string kitPublicKeyPath)
    {
        using var key = ReleaseKey.Load(keyPath);
        if (File.Exists(kitPublicKeyPath))
        {
            if (!SamePem(File.ReadAllText(kitPublicKeyPath), key.PublicKeyPem))
            {
                throw new KitError($"{keyPath} does not match the public half");
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(kitPublicKeyPath))!);
            File.WriteAllText(kitPublicKeyPath, key.PublicKeyPem, new UTF8Encoding(false));
            Console.WriteLine($"kit panel key (public half) written to {kitPublicKeyPath}");
        }
        var sums = Encoding.ASCII.GetBytes(Sums(panelDir));
        File.WriteAllBytes(Path.Combine(panelDir, FrameKitConstants.PanelSums), sums);
        File.WriteAllBytes(Path.Combine(panelDir, FrameKitConstants.PanelSignature), key.Sign(sums));
        return File.ReadAllText(Path.Combine(panelDir, FrameKitConstants.PanelVersionFile)).Trim();
    }

    public static string? Problem(string panelDir, string kitPublicKeyPem)
    {
        var sumsPath = Path.Combine(panelDir, FrameKitConstants.PanelSums);
        var signaturePath = Path.Combine(panelDir, FrameKitConstants.PanelSignature);
        if (!File.Exists(sumsPath) || !File.Exists(signaturePath))
        {
            return "shared panel is not signed";
        }
        var sums = File.ReadAllBytes(sumsPath);
        bool verified;
        try
        {
            verified = ReleaseKey.Verify(kitPublicKeyPem, sums, File.ReadAllBytes(signaturePath));
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return "cannot read shared panel key";
        }
        return verified && Encoding.ASCII.GetString(sums) == Sums(panelDir) ? null : "shared panel signature is invalid";
    }

    private static bool SamePem(string a, string b) => a.Replace("\r\n", "\n").Trim() == b.Replace("\r\n", "\n").Trim();
}

namespace FrameKit;

public static class KitPaths
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    public static string Root => RootPath.Value;
    public static string PackageScripts => Path.Combine(Root, "package");
    public static string PanelBuild => Path.Combine(Root, "panel", "build", FrameKitConstants.PanelBinary);
    public static string PanelPublicKey => Path.Combine(PackageScripts, FrameKitConstants.PanelKey);
    public static string SetupProject => Path.Combine(Root, "setup", "src", "FrameSetup", "FrameSetup.csproj");
    public static string Common => Path.Combine(Root, "common");

    public static string DefaultKey =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".framekit", "release-key.pem");

    public static string DefaultPanelKey =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".framekit", "panel-key.pem");

    private static string FindRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "package", FrameKitConstants.InstallScript)) &&
                    File.Exists(Path.Combine(dir.FullName, "setup", "src", "FrameSetup", "FrameSetup.csproj")))
                {
                    return dir.FullName;
                }
            }
        }
        throw new KitError("cannot find the FrameKit folder");
    }
}

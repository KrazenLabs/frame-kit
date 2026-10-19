using Avalonia;

namespace FrameKit.Setup.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--self-check")
        {
            return SelfCheck(args[1]);
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    private static int SelfCheck(string reportPath)
    {
        try
        {
            var assembly = typeof(Program).Assembly;
            var package = AppPackage.FromAssembly(assembly);
            var branding = SetupBranding.FromAssembly(assembly);
            if (package is null)
            {
                File.WriteAllText(reportPath, "error no package embedded\n");
                return 1;
            }
            var m = package.Manifest;
            var icon = AppIcon.Describe(branding.Icon);
            File.WriteAllText(reportPath,
                $"ok {m.Id} {m.Version}\nname {m.Name}\nfile {package.FileName}\nsha256 {package.Sha256}\n" +
                $"panel {m.PanelVersion}\nicon {icon}\nfound_it {branding.FoundIt}\n");
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(reportPath, "error " + exception.Message + "\n");
            return 1;
        }
    }
}

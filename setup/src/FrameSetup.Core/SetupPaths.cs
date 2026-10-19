namespace FrameKit.Setup;

public static class SetupPaths
{
    public const string HomeOverrideVariable = "FRAMEKIT_SETUP_HOME";

    public static string ConfigDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(HomeOverrideVariable);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return overridden;
            }
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrameKit", "Setup");
            }
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDirectory = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdg;
            return Path.Combine(baseDirectory, "framekit", "setup");
        }
    }
}

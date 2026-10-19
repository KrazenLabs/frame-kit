namespace FrameKit.Setup;

public static class InstallChecks
{
    public const long SpaceMarginMb = FrameKitConstants.SpaceMarginMb;

    public static long RequiredFreeMb(AppManifest manifest) => manifest.FreeMb + SpaceMarginMb;

    public static bool OsTooOld(AppManifest manifest, DeviceInfo device) =>
        device.OsVersion.Length > 0 && VersionOrder.Compare(device.OsVersion, manifest.MinOsVersion) < 0;

    public static bool TooLittleSpace(AppManifest manifest, DeviceInfo device) =>
        device.FreeMegabytes >= 0 && device.FreeMegabytes < RequiredFreeMb(manifest);
}

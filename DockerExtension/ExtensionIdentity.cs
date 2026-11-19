namespace DockerExtension;

// Release builds are the Store package; Debug builds get their own package name and
// COM class id so a dev copy installs next to the Store copy. The csproj reads the Dev* values.
internal static class ExtensionIdentity
{
    internal const string ReleaseClsid = "935FCF37-1EA1-48EC-AD60-1DA760ACA9D1";

    internal const string DevClsid = "BC4CBF9E-346C-4418-AB98-BB8E95DF2743";

    internal const string DevPackageName = "BambinoSK.DockerForCommandPalette.Dev";

    internal const string DevDisplayName = "Docker (Dev)";

#if DEBUG
    internal const string Clsid = DevClsid;
    internal const string DisplayName = DevDisplayName;
    internal const string ProviderId = "docker-dev";
    internal const string StorageName = "DockerExtension.Dev";
#else
    internal const string Clsid = ReleaseClsid;
    internal const string DisplayName = "Docker";
    internal const string ProviderId = "docker";
    internal const string StorageName = "DockerExtension";
#endif
}

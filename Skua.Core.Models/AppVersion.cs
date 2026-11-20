namespace Skua.Core.Models;

public static class AppVersion
{
    public const int Major = 2;
    public const int Minor = 1;
    public const int Patch = 0;
    public const string Suffix = "linux";

    public static string Version => $"{Major}.{Minor}.{Patch}";
    public static string ReleaseTag => $"v{Version}";
    public static string DisplayVersion => $"v{Version}";
    public static string FullTitle => $"Skua Linux v{Version} — Native .NET 10";
}

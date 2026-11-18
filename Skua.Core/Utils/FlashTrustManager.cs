namespace Skua.Core.Utils;

public static class FlashTrustManager
{
    private static readonly string TrustFolderPath = OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Macromedia", "Flash Player", "#Security", "FlashPlayerTrust")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".macromedia", "Flash_Player", "#Security", "FlashPlayerTrust");

    private static readonly string TrustFileName = "Skua.cfg";

    public static void EnsureTrustFile()
    {
        try
        {
            Directory.CreateDirectory(TrustFolderPath);

            if (!OperatingSystem.IsWindows())
            {
                string mmsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".macromedia", "Flash_Player");
                string mmsCfg = Path.Combine(mmsDir, "mms.cfg");
                if (!File.Exists(mmsCfg))
                {
                    File.WriteAllLines(mmsCfg, new[]
                    {
                        "OverrideGPUValidation=1",
                        "EnableLinuxHWVideoDecode=1"
                    });
                }
            }

            string trustFilePath = Path.Combine(TrustFolderPath, TrustFileName);
            string appDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string skuaDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Skua");

            string[] requiredPaths = { appDirectory, skuaDataDirectory };

            if (File.Exists(trustFilePath))
            {
                string[] existingPaths = File.ReadAllLines(trustFilePath);
                bool needsUpdate = false;

                foreach (string path in requiredPaths)
                {
                    bool found = false;
                    foreach (string existingPath in existingPaths)
                    {
                        if (existingPath.Trim().Equals(path, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        needsUpdate = true;
                        break;
                    }
                }

                if (!needsUpdate)
                    return;
            }

            File.WriteAllLines(trustFilePath, requiredPaths);
        }
        catch
        {
            /* ignored */
        }
    }
}

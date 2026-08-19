namespace ProjectPublisher.Services;

public static class AppPaths
{
    public static string Root => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectPublisher"));

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string StagingRoot => Ensure(Path.Combine(Root, "staging"));
    public static string PreviewRoot => Ensure(Path.Combine(Root, "previews"));

    public static string CreateTemporaryDirectory(string category)
    {
        var path = Path.Combine(Root, category, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void CleanupAbandonedWorkspaces()
    {
        CleanupChildren(Path.Combine(Root, "staging"), deleteAll: true);
        CleanupChildren(Path.Combine(Root, "screenshot-work"), deleteAll: true);
        CleanupChildren(Path.Combine(Root, "previews"), deleteAll: false);
    }

    public static void TryDeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* Continue cleanup. */ }
            }
            Directory.Delete(path, true);
        }
        catch
        {
            // A child process may still hold a file. Startup cleanup retries next launch.
        }
    }

    private static void CleanupChildren(string parent, bool deleteAll)
    {
        if (!Directory.Exists(parent)) return;
        foreach (var directory in Directory.EnumerateDirectories(parent))
        {
            if (deleteAll || Directory.GetCreationTimeUtc(directory) < DateTime.UtcNow.AddDays(-7))
                TryDeleteTree(directory);
        }
        if (!deleteAll)
        {
            foreach (var file in Directory.EnumerateFiles(parent))
            {
                try
                {
                    if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) File.Delete(file);
                }
                catch { /* Retry through normal user cleanup. */ }
            }
        }
    }

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}

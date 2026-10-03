namespace QueueManagement.Api;

public static class DatabasePathProvider
{
    public static string ResolveDatabasePath(string? startingPath = null)
    {
        var rootPath = startingPath ?? Directory.GetCurrentDirectory();
        var current = new DirectoryInfo(rootPath);

        while (current is not null)
        {
            if (Directory.EnumerateFiles(current.FullName, "*.slnx", SearchOption.TopDirectoryOnly).Any() ||
                Directory.EnumerateFiles(current.FullName, "*.sln", SearchOption.TopDirectoryOnly).Any())
            {
                var dbDirectory = Path.Combine(current.FullName, "storage");
                Directory.CreateDirectory(dbDirectory);
                return Path.Combine(dbDirectory, "QueueManagement.db");
            }

            current = current.Parent;
        }

        var fallbackDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(fallbackDirectory);
        return Path.Combine(fallbackDirectory, "QueueManagement.db");
    }
}

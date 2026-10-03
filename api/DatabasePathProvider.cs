namespace QueueManagement.Api;

public static class DatabasePathProvider
{
    public static string ResolveDatabasePath(string? startingPath = null)
    {
        // 1. Explicit override via environment variable (ideal for containerised deployments)
        var envPath = Environment.GetEnvironmentVariable("RAPIDQ_DB_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);
            return envPath;
        }

        // 2. Storage folder adjacent to the app root (works in production Docker: /app/storage)
        var rootPath = startingPath ?? AppContext.BaseDirectory;
        var storageSibling = Path.Combine(rootPath, "storage");
        if (Directory.Exists(storageSibling) || !IsRunningLocally())
        {
            Directory.CreateDirectory(storageSibling);
            return Path.Combine(storageSibling, "QueueManagement.db");
        }

        // 3. Walk up to find the solution file (works in local development)
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

        // 4. Ultimate fallback
        var fallbackDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(fallbackDirectory);
        return Path.Combine(fallbackDirectory, "QueueManagement.db");
    }

    private static bool IsRunningLocally()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}

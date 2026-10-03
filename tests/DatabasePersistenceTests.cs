using QueueManagement.Api;

namespace QueueManagement.Tests;

public class DatabasePersistenceTests
{
    [Fact]
    public void GetDatabasePath_ShouldResolveToPersistentProjectStorage()
    {
        var path = DatabasePathProvider.ResolveDatabasePath("C:/Users/kimat/Downloads/Queue/src/QueueManagement.Api");

        Assert.EndsWith("QueueManagement.db", path);
        Assert.Contains("Queue", Path.GetDirectoryName(path)!);
        Assert.DoesNotContain("bin", path, StringComparison.OrdinalIgnoreCase);
    }
}

using QueueManagement.Api;
using QueueManagement.Api.Data;
using QueueManagement.Shared;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public async Task SqliteDatabase_PersistsAtLeastSixtyAppointmentsAcrossContexts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rapidq-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<QueueDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;

        try
        {
            using (var db = new QueueDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.Appointments.AddRange(Enumerable.Range(1, 60).Select(number => new Appointment
                {
                    CustomerName = $"Customer {number}",
                    CustomerPhone = "555-0100",
                    ServiceId = 1,
                    BranchId = 1,
                    AppointmentDate = DateTime.Today,
                    TimeSlot = "09:00",
                    QueueNumber = number,
                    QueueCode = $"TE-{number:D4}",
                    Status = AppointmentStatus.Waiting,
                    CreatedAt = DateTime.UtcNow
                }));
                await db.SaveChangesAsync();
            }

            using (var reopenedDb = new QueueDbContext(options))
            {
                Assert.Equal(60, await reopenedDb.Appointments.CountAsync());
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var file = path + suffix;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}

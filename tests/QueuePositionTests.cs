using QueueManagement.Api;
using QueueManagement.Shared;

namespace QueueManagement.Tests;

public class QueuePositionTests
{
    [Fact]
    public void PeopleAheadPredicate_CountsEarlierActiveTicketsAcrossServices()
    {
        var ticket = new Appointment { BranchId = 1, ServiceId = 2, QueueNumber = 5 };
        var appointments = new[]
        {
            new Appointment { BranchId = 1, ServiceId = 1, QueueNumber = 1, Status = AppointmentStatus.Waiting },
            new Appointment { BranchId = 1, ServiceId = 3, QueueNumber = 2, Status = AppointmentStatus.Called },
            new Appointment { BranchId = 1, ServiceId = 2, QueueNumber = 3, Status = AppointmentStatus.Serving },
            new Appointment { BranchId = 1, ServiceId = 1, QueueNumber = 4, Status = AppointmentStatus.Served },
            new Appointment { BranchId = 2, ServiceId = 2, QueueNumber = 2, Status = AppointmentStatus.Waiting },
            new Appointment { BranchId = 1, ServiceId = 2, QueueNumber = 5, Status = AppointmentStatus.Waiting },
            new Appointment { BranchId = 1, ServiceId = 2, QueueNumber = 6, Status = AppointmentStatus.Waiting }
        };

        var predicate = QueuePositionCalculator.PeopleAheadPredicate(ticket).Compile();

        Assert.Equal(3, appointments.Count(predicate));
    }
}
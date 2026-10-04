using System.Linq.Expressions;
using QueueManagement.Shared;

namespace QueueManagement.Api;

public static class QueuePositionCalculator
{
    public static Expression<Func<Appointment, bool>> PeopleAheadPredicate(Appointment appointment)
    {
        var activeStatuses = new[]
        {
            AppointmentStatus.Waiting,
            AppointmentStatus.Called,
            AppointmentStatus.Serving
        };

        return candidate =>
            candidate.BranchId == appointment.BranchId &&
            candidate.QueueNumber < appointment.QueueNumber &&
            activeStatuses.Contains(candidate.Status);
    }
}
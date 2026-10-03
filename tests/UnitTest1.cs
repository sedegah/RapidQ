using QueueManagement.Shared;

namespace QueueManagement.Tests;

public class UnitTest1
{
    [Fact]
    public void WeekendDates_AreRejected()
    {
        var saturday = new DateTime(2026, 9, 26);
        var sunday = new DateTime(2026, 9, 27);

        Assert.False(AppointmentScheduling.IsBusinessDay(saturday));
        Assert.False(AppointmentScheduling.IsBusinessDay(sunday));
    }

    [Fact]
    public void OfficeHours_AreValidated()
    {
        Assert.True(AppointmentScheduling.IsWithinBusinessHours("09:00"));
        Assert.True(AppointmentScheduling.IsWithinBusinessHours("15:30"));
        Assert.False(AppointmentScheduling.IsWithinBusinessHours("08:20"));
        Assert.False(AppointmentScheduling.IsWithinBusinessHours("17:30"));
    }

    [Fact]
    public void PreferredTime_UsesSelectedSlot_WhenValid()
    {
        var appointmentDate = new DateTime(2026, 9, 29);

        var expected = AppointmentScheduling.GetExpectedAppointmentTime(appointmentDate, "08:20");

        Assert.Null(expected);

        var validExpected = AppointmentScheduling.GetExpectedAppointmentTime(appointmentDate, "09:15");

        Assert.Equal(new DateTime(2026, 9, 29, 9, 15, 0), validExpected);
    }
}

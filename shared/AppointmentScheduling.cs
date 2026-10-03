namespace QueueManagement.Shared;

public static class AppointmentScheduling
{
    public static readonly TimeOnly OpeningTime = new(9, 0);
    public static readonly TimeOnly ClosingTime = new(17, 0);

    public static bool IsBusinessDay(DateTime date)
    {
        return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
    }

    public static bool IsWithinBusinessHours(TimeOnly time)
    {
        return time >= OpeningTime && time < ClosingTime;
    }

    public static bool IsWithinBusinessHours(string? timeValue)
    {
        if (string.IsNullOrWhiteSpace(timeValue))
        {
            return false;
        }

        if (!TimeOnly.TryParse(timeValue, out var time))
        {
            return false;
        }

        return IsWithinBusinessHours(time);
    }

    public static DateTime? GetExpectedAppointmentTime(DateTime appointmentDate, string? timeValue)
    {
        if (!IsBusinessDay(appointmentDate) || string.IsNullOrWhiteSpace(timeValue))
        {
            return null;
        }

        if (!TimeOnly.TryParse(timeValue, out var preferredTime) || !IsWithinBusinessHours(preferredTime))
        {
            return null;
        }

        return appointmentDate.Date.AddHours(preferredTime.Hour).AddMinutes(preferredTime.Minute);
    }
}
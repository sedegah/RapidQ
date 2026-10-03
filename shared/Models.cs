namespace QueueManagement.Shared;

public enum AppointmentStatus
{
    Waiting,
    Called,
    Serving,
    Served,
    Missed
}

public class Branch
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}

public class ServiceItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ServiceCode { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
}

public class Appointment
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int ServiceId { get; set; }
    public ServiceItem? Service { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string TimeSlot { get; set; } = string.Empty;
    public int QueueNumber { get; set; }
    public string QueueCode { get; set; } = string.Empty;
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Waiting;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CalledAt { get; set; }
    public DateTime? ServedAt { get; set; }
}

public class CreateAppointmentRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int ServiceId { get; set; }
    public int BranchId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string TimeSlot { get; set; } = string.Empty;
}

public class QueueViewItem
{
    public int Id { get; set; }
    public int QueueNumber { get; set; }
    public string QueueCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string TimeSlot { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public AppointmentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CalledAt { get; set; }
}

public class DashboardSummary
{
    public int TotalAppointments { get; set; }
    public int ActiveQueue { get; set; }
    public int StaffCallsToday { get; set; }
    public int ServicesAvailable { get; set; }
    public int AverageWaitMinutes { get; set; }
}

public class AppointmentHistoryItem
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string QueueCode { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public string TimeSlot { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ServedAt { get; set; }
    public int? ServiceDurationMinutes { get; set; }
    public AppointmentStatus Status { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Customer";
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public IList<string> Roles { get; set; } = new List<string>();
}

public class ServiceDistributionItem
{
    public string ServiceName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class AdminAnalyticsResponse
{
    public DashboardSummary Summary { get; set; } = new();
    public List<ServiceDistributionItem> ServiceDistribution { get; set; } = new();
}

public class TrackResponse
{
    public string QueueCode { get; set; } = string.Empty;
    public AppointmentStatus Status { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public DateTime ExpectedTime { get; set; }
    public int PeopleAhead { get; set; }
}

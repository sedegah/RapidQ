using Microsoft.AspNetCore.Identity;
using QueueManagement.Shared;
using System.Text.Json;

namespace QueueManagement.Api.Data;

public sealed class RapidQRepository
{
    private readonly CloudflareD1Client _d1;
    private readonly PasswordHasher<D1User> _passwordHasher = new();

    public RapidQRepository(CloudflareD1Client d1) => _d1 = d1;

    public Task<List<Branch>> GetBranchesAsync(CancellationToken ct = default) =>
        _d1.QueryAsync<Branch>("SELECT id AS Id, name AS Name, location AS Location FROM branches ORDER BY name LIMIT 1", ct);

    public Task<List<Branch>> GetAllBranchesAsync(CancellationToken ct = default) =>
        _d1.QueryAsync<Branch>("SELECT id AS Id, name AS Name, location AS Location FROM branches ORDER BY name", ct);

    public Task<List<ServiceItem>> GetServicesAsync(CancellationToken ct = default) =>
        _d1.QueryAsync<ServiceItem>("SELECT id AS Id, name AS Name, description AS Description, service_code AS ServiceCode, branch_id AS BranchId FROM services ORDER BY name", ct);

    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();
        var role = request.Role.Trim();
        if (email.Length == 0 || request.Password.Length < 6 || role is not ("Customer" or "Staff" or "Admin"))
        {
            return new RegistrationResult(false, "Email, password, or role is invalid.");
        }

        if (role == "Admin")
        {
            return new RegistrationResult(false, "Admin accounts cannot be registered publicly.");
        }

        var user = new D1User { Id = Guid.NewGuid().ToString("N"), Email = email };
        var hash = _passwordHasher.HashPassword(user, request.Password);
        try
        {
            await _d1.ExecuteBatchAsync(new[]
            {
                new D1Statement("INSERT INTO users (id, email, normalized_email, password_hash, created_at) VALUES (?, ?, ?, ?, ?)",
                    new object?[] { user.Id, email, normalizedEmail, hash, DateTime.UtcNow.ToString("O") }),
                new D1Statement("INSERT INTO user_roles (user_id, role_id) SELECT ?, id FROM roles WHERE name = ?",
                    new object?[] { user.Id, role })
            }, ct);
            return new RegistrationResult(true, null);
        }
        catch (HttpRequestException)
        {
            return new RegistrationResult(false, "An account with this email may already exist.");
        }
    }

    public async Task EnsureBootstrapAdminAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var existing = await _d1.QueryAsync<ExistsRow>("SELECT 1 AS Value FROM users WHERE normalized_email = ? LIMIT 1", ct, normalizedEmail);
        if (existing.Count > 0) return;

        var user = new D1User { Id = Guid.NewGuid().ToString("N"), Email = email.Trim() };
        var hash = _passwordHasher.HashPassword(user, password);
        await _d1.ExecuteBatchAsync(new[]
        {
            new D1Statement("INSERT INTO users (id, email, normalized_email, password_hash, created_at) VALUES (?, ?, ?, ?, ?)",
                new object?[] { user.Id, user.Email, normalizedEmail, hash, DateTime.UtcNow.ToString("O") }),
            new D1Statement("INSERT INTO user_roles (user_id, role_id) SELECT ?, id FROM roles WHERE name = 'Admin'",
                new object?[] { user.Id })
        }, ct);
    }

    public async Task<AuthenticatedAccount?> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        var users = await _d1.QueryAsync<D1User>(
            "SELECT id AS Id, email AS Email, password_hash AS PasswordHash FROM users WHERE normalized_email = ? LIMIT 1",
            ct, email.Trim().ToUpperInvariant());
        var user = users.FirstOrDefault();
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return null;
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var replacementHash = _passwordHasher.HashPassword(user, password);
            await _d1.ExecuteAsync("UPDATE users SET password_hash = ? WHERE id = ?", ct, replacementHash, user.Id);
        }

        var roles = await _d1.QueryAsync<RoleName>(
            "SELECT r.name AS Name FROM roles r JOIN user_roles ur ON ur.role_id = r.id WHERE ur.user_id = ? ORDER BY r.name",
            ct, user.Id);
        return new AuthenticatedAccount(user.Email, roles.Select(role => role.Name).ToArray());
    }

    public async Task<(QueueTicketRecord? Ticket, ServiceItem? Service, Branch? Branch)> CreateTicketAsync(CreateAppointmentRequest request, CancellationToken ct = default)
    {
        var services = await _d1.QueryAsync<ServiceItem>(
            "SELECT id AS Id, name AS Name, description AS Description, service_code AS ServiceCode, branch_id AS BranchId FROM services WHERE id = ? LIMIT 1",
            ct, request.ServiceId);
        var service = services.FirstOrDefault();
        if (service is null) return (null, null, null);

        var branches = await _d1.QueryAsync<Branch>(
            "SELECT id AS Id, name AS Name, location AS Location FROM branches ORDER BY id LIMIT 1", ct);
        var branch = branches.FirstOrDefault();
        if (branch is null) return (null, service, null);

        var issuedAt = DateTime.UtcNow;
        var date = request.AppointmentDate.ToString("O");
        var timeSlot = string.IsNullOrWhiteSpace(request.TimeSlot) ? "Walk-in" : request.TimeSlot;
        var tickets = await _d1.QueryAsync<QueueTicketRecord>(
            "WITH next_number AS (SELECT COALESCE(MAX(queue_number), 0) + 1 AS value FROM tickets) " +
            "INSERT INTO tickets (customer_name, customer_email, customer_phone, service_id, branch_id, appointment_date, time_slot, queue_number, queue_code, status, created_at) " +
            "SELECT ?, ?, ?, s.id, b.id, ?, ?, n.value, SUBSTR(REPLACE(UPPER(s.service_code), '-', ''), 1, 2) || '-' || PRINTF('%04d', n.value), 0, ? " +
            "FROM services s JOIN branches b ON b.id = ? CROSS JOIN next_number n WHERE s.id = ? " +
            "RETURNING id AS Id, queue_number AS QueueNumber, queue_code AS QueueCode, status AS Status, created_at AS CreatedAt",
            ct, request.CustomerName.Trim(), request.CustomerEmail.Trim(), request.CustomerPhone.Trim(), date, timeSlot, issuedAt.ToString("O"), branch.Id, service.Id);
        return (tickets.FirstOrDefault(), service, branch);
    }

    public async Task<TrackResponse?> TrackAsync(string queueCode, CancellationToken ct = default)
    {
        var rows = await _d1.QueryAsync<TrackRow>(
            "SELECT t.queue_code AS QueueCode, t.status AS Status, s.name AS ServiceName, t.appointment_date AS ExpectedTime, " +
            "(SELECT COUNT(*) FROM tickets ahead WHERE ahead.branch_id = t.branch_id AND ahead.queue_number < t.queue_number AND ahead.status IN (0, 1, 2)) AS PeopleAhead " +
            "FROM tickets t JOIN services s ON s.id = t.service_id WHERE t.queue_code = ? LIMIT 1",
            ct, queueCode);
        var row = rows.FirstOrDefault();
        return row is null ? null : new TrackResponse
        {
            QueueCode = row.QueueCode,
            Status = (AppointmentStatus)row.Status,
            ServiceName = row.ServiceName,
            ExpectedTime = DateTime.Parse(row.ExpectedTime, null, System.Globalization.DateTimeStyles.RoundtripKind),
            PeopleAhead = row.PeopleAhead
        };
    }

    public Task<List<QueueViewItem>> GetQueueAsync(CancellationToken ct = default) =>
        _d1.QueryAsync<QueueViewItem>(
            "SELECT t.id AS Id, t.queue_number AS QueueNumber, t.queue_code AS QueueCode, t.customer_name AS CustomerName, " +
            "t.customer_email AS CustomerEmail, t.customer_phone AS CustomerPhone, s.name AS ServiceName, b.name AS BranchName, " +
            "t.time_slot AS TimeSlot, t.appointment_date AS AppointmentDate, t.status AS Status, t.created_at AS CreatedAt, t.called_at AS CalledAt " +
            "FROM tickets t JOIN services s ON s.id = t.service_id JOIN branches b ON b.id = t.branch_id " +
            "WHERE t.status NOT IN (3, 4) ORDER BY t.queue_number", ct);

    public async Task<List<AppointmentHistoryItem>> GetHistoryAsync(CancellationToken ct = default)
    {
        var rows = await _d1.QueryAsync<HistoryRow>(
            "SELECT t.id AS Id, t.customer_name AS CustomerName, t.customer_email AS CustomerEmail, t.customer_phone AS CustomerPhone, " +
            "s.name AS ServiceName, s.service_code AS ServiceCode, b.name AS BranchName, t.queue_code AS QueueCode, " +
            "t.appointment_date AS AppointmentDate, t.time_slot AS TimeSlot, t.created_at AS CreatedAt, t.called_at AS CalledAt, " +
            "t.served_at AS ServedAt, t.status AS Status FROM tickets t JOIN services s ON s.id = t.service_id " +
            "JOIN branches b ON b.id = t.branch_id WHERE t.status IN (3, 4) ORDER BY COALESCE(t.served_at, t.created_at) DESC", ct);
        return rows.Select(row => new AppointmentHistoryItem
        {
            Id = row.Id,
            CustomerName = row.CustomerName,
            CustomerEmail = row.CustomerEmail,
            CustomerPhone = row.CustomerPhone,
            ServiceName = row.ServiceName,
            ServiceCode = row.ServiceCode,
            BranchName = row.BranchName,
            QueueCode = row.QueueCode,
            AppointmentDate = ParseDate(row.AppointmentDate),
            TimeSlot = row.TimeSlot,
            CreatedAt = ParseDate(row.CreatedAt),
            ServedAt = string.IsNullOrWhiteSpace(row.ServedAt) ? null : ParseDate(row.ServedAt),
            ServiceDurationMinutes = row.CalledAt is not null && row.ServedAt is not null
                ? (int)Math.Max(0, (ParseDate(row.ServedAt) - ParseDate(row.CalledAt)).TotalMinutes)
                : null,
            Status = (AppointmentStatus)row.Status
        }).ToList();
    }

    public async Task<Appointment?> AdvanceTicketAsync(int id, string action, CancellationToken ct = default)
    {
        var (newStatus, allowedStatuses, setCalled, setServed, clearCalled) = action switch
        {
            "call" => (AppointmentStatus.Called, new[] { AppointmentStatus.Waiting }, true, false, false),
            "serve" => (AppointmentStatus.Served, new[] { AppointmentStatus.Called, AppointmentStatus.Serving }, false, true, false),
            "skip" => (AppointmentStatus.Missed, new[] { AppointmentStatus.Waiting, AppointmentStatus.Called }, false, false, false),
            "recall" => (AppointmentStatus.Waiting, new[] { AppointmentStatus.Served, AppointmentStatus.Missed }, false, false, true),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        var allowedSql = string.Join(",", allowedStatuses.Select(status => (int)status));
        var updates = new List<string> { "status = ?" };
        var parameters = new List<object?> { (int)newStatus };
        if (setCalled) updates.Add("called_at = COALESCE(called_at, ?)");
        if (setCalled) parameters.Add(DateTime.UtcNow.ToString("O"));
        if (setServed) updates.Add("served_at = COALESCE(served_at, ?)");
        if (setServed) parameters.Add(DateTime.UtcNow.ToString("O"));
        if (clearCalled) updates.Add("called_at = NULL");
        parameters.Add(id);

        var rows = await _d1.QueryAsync<TicketRow>(
            $"UPDATE tickets SET {string.Join(", ", updates)} WHERE id = ? AND status IN ({allowedSql}) " +
            "RETURNING id AS Id, customer_name AS CustomerName, customer_email AS CustomerEmail, customer_phone AS CustomerPhone, " +
            "service_id AS ServiceId, branch_id AS BranchId, appointment_date AS AppointmentDate, time_slot AS TimeSlot, " +
            "queue_number AS QueueNumber, queue_code AS QueueCode, status AS Status, created_at AS CreatedAt, called_at AS CalledAt, served_at AS ServedAt",
            ct, parameters.ToArray());
        var row = rows.FirstOrDefault();
        return row is null ? null : row.ToAppointment();
    }

    public async Task<bool> TicketExistsAsync(int id, CancellationToken ct = default) =>
        (await _d1.QueryAsync<ExistsRow>("SELECT 1 AS Value FROM tickets WHERE id = ? LIMIT 1", ct, id)).Count > 0;

    public async Task<DashboardSummary> GetDashboardAsync(CancellationToken ct = default)
    {
        var tickets = await _d1.QueryAsync<StatusCreatedRow>("SELECT status AS Status, created_at AS CreatedAt FROM tickets", ct);
        var active = tickets.Count(ticket => ticket.Status is not 3 and not 4);
        return new DashboardSummary
        {
            TotalAppointments = tickets.Count,
            ActiveQueue = active,
            StaffCallsToday = tickets.Count(ticket => ticket.Status is 1 or 2),
            ServicesAvailable = (await _d1.QueryAsync<CountRow>("SELECT COUNT(*) AS Count FROM services", ct)).FirstOrDefault()?.Count ?? 0,
            AverageWaitMinutes = active == 0 ? 0 : Math.Max(5, active * 7)
        };
    }

    public async Task<object> GetStaffAnalyticsAsync(CancellationToken ct = default)
    {
        var tickets = await _d1.QueryAsync<AnalyticsRow>(
            "SELECT t.status AS Status, t.created_at AS CreatedAt, s.name AS ServiceName FROM tickets t JOIN services s ON s.id = t.service_id", ct);
        var today = DateTime.UtcNow.Date;
        var todayTickets = tickets.Where(ticket => ParseDate(ticket.CreatedAt).Date == today).ToList();
        var active = tickets.Count(ticket => ticket.Status is not 3 and not 4);
        var summary = new DashboardSummary
        {
            TotalAppointments = tickets.Count,
            ActiveQueue = active,
            StaffCallsToday = tickets.Count(ticket => ticket.Status is 1 or 2),
            ServicesAvailable = (await _d1.QueryAsync<CountRow>("SELECT COUNT(*) AS Count FROM services", ct)).FirstOrDefault()?.Count ?? 0,
            AverageWaitMinutes = active == 0 ? 0 : Math.Max(5, active * 7)
        };
        var distribution = tickets.GroupBy(ticket => ticket.ServiceName)
            .Select(group => new ServiceDistributionItem { ServiceName = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count).ToList();
        return new
        {
            Summary = summary,
            ServiceDistribution = distribution,
            StatusBreakdown = new
            {
                Waiting = tickets.Count(ticket => ticket.Status == 0),
                Called = tickets.Count(ticket => ticket.Status == 1),
                Serving = tickets.Count(ticket => ticket.Status == 2),
                ServedToday = todayTickets.Count(ticket => ticket.Status == 3),
                MissedToday = todayTickets.Count(ticket => ticket.Status == 4)
            }
        };
    }

    public async Task<AdminAnalyticsResponse> GetAdminAnalyticsAsync(CancellationToken ct = default)
    {
        var analytics = await GetStaffAnalyticsAsync(ct);
        var json = System.Text.Json.JsonSerializer.Serialize(analytics);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var summary = doc.RootElement.GetProperty("summary").Deserialize<DashboardSummary>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
        var distribution = doc.RootElement.GetProperty("serviceDistribution").Deserialize<List<ServiceDistributionItem>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
        return new AdminAnalyticsResponse { Summary = summary, ServiceDistribution = distribution };
    }

    public async Task<List<ServiceItem>> AdminServicesAsync(CancellationToken ct = default) => await GetServicesAsync(ct);

    public async Task<int?> AddServiceAsync(ServiceItem service, CancellationToken ct = default)
    {
        var rows = await _d1.QueryAsync<IdRow>(
            "INSERT INTO services (name, description, service_code, branch_id) VALUES (?, ?, ?, ?) RETURNING id AS Id",
            ct, service.Name.Trim(), service.Description ?? string.Empty, service.ServiceCode.Trim(), service.BranchId);
        return rows.FirstOrDefault()?.Id;
    }

    public Task<D1WriteResult> UpdateServiceAsync(int id, ServiceItem service, CancellationToken ct = default) =>
        _d1.ExecuteAsync("UPDATE services SET name = ?, description = ?, service_code = ?, branch_id = ? WHERE id = ?",
            ct, service.Name.Trim(), service.Description ?? string.Empty, service.ServiceCode.Trim(), service.BranchId, id);

    public Task<D1WriteResult> DeleteServiceAsync(int id, CancellationToken ct = default) =>
        _d1.ExecuteAsync("DELETE FROM services WHERE id = ?", ct, id);

    public async Task<int?> AddBranchAsync(Branch branch, CancellationToken ct = default)
    {
        var rows = await _d1.QueryAsync<IdRow>("INSERT INTO branches (name, location) VALUES (?, ?) RETURNING id AS Id", ct, branch.Name.Trim(), branch.Location.Trim());
        return rows.FirstOrDefault()?.Id;
    }

    public Task<D1WriteResult> UpdateBranchAsync(int id, Branch branch, CancellationToken ct = default) =>
        _d1.ExecuteAsync("UPDATE branches SET name = ?, location = ? WHERE id = ?", ct, branch.Name.Trim(), branch.Location.Trim(), id);

    public Task<D1WriteResult> DeleteBranchAsync(int id, CancellationToken ct = default) =>
        _d1.ExecuteAsync("DELETE FROM branches WHERE id = ?", ct, id);

    private static DateTime ParseDate(string value) => DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private sealed class D1User { public string Id { get; set; } = string.Empty; public string Email { get; set; } = string.Empty; public string PasswordHash { get; set; } = string.Empty; }
    private sealed class RoleName { public string Name { get; set; } = string.Empty; }
    private sealed class ExistsRow { public int Value { get; set; } }
    private sealed class CountRow { public int Count { get; set; } }
    private sealed class IdRow { public int Id { get; set; } }
    private sealed class StatusCreatedRow { public int Status { get; set; } public string CreatedAt { get; set; } = string.Empty; }
    private sealed class AnalyticsRow { public int Status { get; set; } public string CreatedAt { get; set; } = string.Empty; public string ServiceName { get; set; } = string.Empty; }
    private sealed class TrackRow { public string QueueCode { get; set; } = string.Empty; public int Status { get; set; } public string ServiceName { get; set; } = string.Empty; public string ExpectedTime { get; set; } = string.Empty; public int PeopleAhead { get; set; } }
    private sealed class HistoryRow
    {
        public int Id { get; set; } public string CustomerName { get; set; } = string.Empty; public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty; public string ServiceName { get; set; } = string.Empty; public string ServiceCode { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty; public string QueueCode { get; set; } = string.Empty; public string AppointmentDate { get; set; } = string.Empty;
        public string TimeSlot { get; set; } = string.Empty; public string CreatedAt { get; set; } = string.Empty; public string? CalledAt { get; set; } public string? ServedAt { get; set; } public int Status { get; set; }
    }
    private sealed class TicketRow
    {
        public int Id { get; set; } public string CustomerName { get; set; } = string.Empty; public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty; public int ServiceId { get; set; } public int BranchId { get; set; } public string AppointmentDate { get; set; } = string.Empty;
        public string TimeSlot { get; set; } = string.Empty; public int QueueNumber { get; set; } public string QueueCode { get; set; } = string.Empty; public int Status { get; set; }
        public string CreatedAt { get; set; } = string.Empty; public string? CalledAt { get; set; } public string? ServedAt { get; set; }
        public Appointment ToAppointment() => new()
        {
            Id = Id, CustomerName = CustomerName, CustomerEmail = CustomerEmail, CustomerPhone = CustomerPhone, ServiceId = ServiceId, BranchId = BranchId,
            AppointmentDate = ParseDate(AppointmentDate), TimeSlot = TimeSlot, QueueNumber = QueueNumber, QueueCode = QueueCode, Status = (AppointmentStatus)Status,
            CreatedAt = ParseDate(CreatedAt), CalledAt = CalledAt is null ? null : ParseDate(CalledAt), ServedAt = ServedAt is null ? null : ParseDate(ServedAt)
        };
    }
}

public sealed record AuthenticatedAccount(string Email, IReadOnlyList<string> Roles);
public sealed record RegistrationResult(bool Succeeded, string? Error);
public sealed class QueueTicketRecord { public int Id { get; set; } public int QueueNumber { get; set; } public string QueueCode { get; set; } = string.Empty; public int Status { get; set; } public string CreatedAt { get; set; } = string.Empty; }

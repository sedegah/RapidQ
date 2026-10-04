using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using QueueManagement.Api.Data;
using QueueManagement.Shared;

namespace QueueManagement.Tests;

public class RapidQRepositoryTests
{
    [Fact]
    public async Task CreateTicket_UsesAtomicGlobalQueueNumberAndKeepsTicketCode()
    {
        var statements = new List<string>();
        var handler = new StubHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var sql = body.RootElement.GetProperty("sql").GetString()!;
            statements.Add(sql);
            if (sql.Contains("FROM services WHERE id", StringComparison.OrdinalIgnoreCase))
                return Json("[{\"Id\":2,\"Name\":\"Cards\",\"Description\":\"\",\"ServiceCode\":\"CARD\",\"BranchId\":1}]");
            if (sql.Contains("FROM branches ORDER BY id", StringComparison.OrdinalIgnoreCase))
                return Json("[{\"Id\":1,\"Name\":\"Main Branch\",\"Location\":\"Head Office\"}]");
            return Json("[{\"Id\":18,\"QueueNumber\":8,\"QueueCode\":\"CA-0008\",\"Status\":0,\"CreatedAt\":\"2026-10-05T08:30:00Z\"}]");
        });
        var repository = CreateRepository(handler);

        var result = await repository.CreateTicketAsync(new CreateAppointmentRequest
        {
            CustomerName = "Sample Customer",
            CustomerPhone = "0712345678",
            ServiceId = 2,
            BranchId = 1,
            AppointmentDate = new DateTime(2026, 10, 5),
            TimeSlot = "09:00"
        });

        Assert.Equal(18, result.Ticket!.Id);
        Assert.Equal("CA-0008", result.Ticket.QueueCode);
        var insert = Assert.Single(statements, sql => sql.Contains("INSERT INTO tickets", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("MAX(queue_number)", insert, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RETURNING", insert, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Track_CountsEarlierActiveTicketsAcrossServicesInBranch()
    {
        string? query = null;
        var repository = CreateRepository(new StubHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            query = body.RootElement.GetProperty("sql").GetString();
            return Json("[{\"QueueCode\":\"CA-0008\",\"Status\":0,\"ServiceName\":\"Cards\",\"ExpectedTime\":\"2026-10-05T09:00:00Z\",\"PeopleAhead\":4}]");
        }));

        var track = await repository.TrackAsync("CA-0008");

        Assert.Equal(4, track!.PeopleAhead);
        Assert.Contains("branch_id = t.branch_id", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("queue_number < t.queue_number", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status IN (0, 1, 2)", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdvanceTicket_ConditionallyChecksPreviousStatus()
    {
        string? query = null;
        var repository = CreateRepository(new StubHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            query = body.RootElement.GetProperty("sql").GetString();
            return Json("[{\"Id\":18,\"CustomerName\":\"A\",\"CustomerEmail\":\"\",\"CustomerPhone\":\"0712345678\",\"ServiceId\":2,\"BranchId\":1,\"AppointmentDate\":\"2026-10-05T00:00:00Z\",\"TimeSlot\":\"09:00\",\"QueueNumber\":8,\"QueueCode\":\"CA-0008\",\"Status\":1,\"CreatedAt\":\"2026-10-05T08:30:00Z\",\"CalledAt\":\"2026-10-05T08:31:00Z\",\"ServedAt\":null}]");
        }));

        var ticket = await repository.AdvanceTicketAsync(18, "call");

        Assert.Equal(AppointmentStatus.Called, ticket!.Status);
        Assert.Contains("status IN (0)", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RETURNING", query, StringComparison.OrdinalIgnoreCase);
    }

    private static RapidQRepository CreateRepository(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cloudflare:AccountId"] = "test-account",
                ["Cloudflare:D1DatabaseId"] = "test-database",
                ["Cloudflare:ApiToken"] = "test-only-token"
            })
            .Build();
        return new RapidQRepository(new CloudflareD1Client(new HttpClient(handler), configuration));
    }

    private static HttpResponseMessage Json(string rows)
    {
        var body = $"{{\"success\":true,\"result\":[{{\"success\":true,\"results\":{rows},\"meta\":{{\"changes\":1,\"last_row_id\":18}}}}]}}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QueueManagement.Api.Data;

public sealed class CloudflareD1Client
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public CloudflareD1Client(HttpClient httpClient, IConfiguration configuration)
    {
        var accountId = configuration["Cloudflare:AccountId"];
        var databaseId = configuration["Cloudflare:D1DatabaseId"];
        var apiToken = configuration["Cloudflare:ApiToken"];

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(databaseId) || string.IsNullOrWhiteSpace(apiToken))
        {
            throw new InvalidOperationException("Configure Cloudflare__AccountId, Cloudflare__D1DatabaseId, and Cloudflare__ApiToken for the API.");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri($"https://api.cloudflare.com/client/v4/accounts/{Uri.EscapeDataString(accountId)}/d1/database/{Uri.EscapeDataString(databaseId)}/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
    }

    public async Task<List<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken = default, params object?[] parameters)
    {
        using var response = await _httpClient.PostAsJsonAsync("query", new D1Query(sql, NormalizeParameters(parameters)), _jsonOptions, cancellationToken);
        using var document = await ReadResponseAsync(response, cancellationToken);
        var statement = GetFirstStatement(document.RootElement);
        var rows = statement.TryGetProperty("results", out var results) ? results : default;
        return rows.ValueKind == JsonValueKind.Array
            ? rows.Deserialize<List<T>>(_jsonOptions) ?? new List<T>()
            : new List<T>();
    }

    public async Task<D1WriteResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default, params object?[] parameters)
    {
        using var response = await _httpClient.PostAsJsonAsync("query", new D1Query(sql, NormalizeParameters(parameters)), _jsonOptions, cancellationToken);
        using var document = await ReadResponseAsync(response, cancellationToken);
        var statement = GetFirstStatement(document.RootElement);
        var metadata = statement.TryGetProperty("meta", out var meta) ? meta : default;
        return new D1WriteResult(
            metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("changes", out var changes) ? changes.GetInt32() : 0,
            metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("last_row_id", out var lastId) && lastId.ValueKind == JsonValueKind.Number ? lastId.GetInt64() : null);
    }

    public async Task ExecuteBatchAsync(IReadOnlyList<D1Statement> statements, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "query",
            new D1BatchQuery(statements.Select(statement => new D1Query(statement.Sql, NormalizeParameters(statement.Parameters))).ToArray()),
            _jsonOptions,
            cancellationToken);
        using var document = await ReadResponseAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array ||
            results.EnumerateArray().Any(result => result.TryGetProperty("success", out var success) && !success.GetBoolean()))
        {
            throw new InvalidOperationException("Cloudflare D1 batch did not complete successfully.");
        }
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || !document.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
        {
            var message = document.RootElement.TryGetProperty("errors", out var errors)
                ? string.Join("; ", errors.EnumerateArray().Select(error => error.TryGetProperty("message", out var text) ? text.GetString() : "D1 query failed"))
                : response.ReasonPhrase;
            document.Dispose();
            throw new HttpRequestException($"Cloudflare D1 request failed: {message}", null, response.StatusCode);
        }

        return document;
    }

    private static JsonElement GetFirstStatement(JsonElement root)
    {
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Cloudflare D1 returned no statement result.");
        }

        var statement = result[0];
        if (statement.TryGetProperty("success", out var success) && !success.GetBoolean())
        {
            throw new InvalidOperationException("Cloudflare D1 statement failed.");
        }

        return statement;
    }

    private static object?[] NormalizeParameters(object?[] parameters) => parameters.Select(value => value switch
    {
        null => null,
        DateTime dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        Enum enumValue => Convert.ToInt64(enumValue, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString()
    }).ToArray();

    private sealed record D1Query(string Sql, object?[] Params);
    private sealed record D1BatchQuery(D1Query[] Batch);
}

public sealed record D1WriteResult(int Changes, long? LastRowId);
public sealed record D1Statement(string Sql, object?[] Parameters);

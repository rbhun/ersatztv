using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErsatzTV.Core;
using ErsatzTV.Core.Interfaces.Updates;
using ErsatzTV.Core.Updates;
using Microsoft.Extensions.Logging;

namespace ErsatzTV.Infrastructure.Updates;

public class UpdateAgentClient(IHttpClientFactory httpClientFactory, ILogger<UpdateAgentClient> logger)
    : IUpdateAgentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsConfigured
    {
        get
        {
            string url = Environment.GetEnvironmentVariable("ETV_UPDATE_AGENT_URL");
            string token = Environment.GetEnvironmentVariable("ETV_UPDATE_AGENT_TOKEN");
            return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(token);
        }
    }

    public Task<Either<BaseError, UpdateSourcesResponse>> GetSources(CancellationToken cancellationToken) =>
        GetAsync<UpdateSourcesResponse>("/v1/sources", cancellationToken);

    public Task<Either<BaseError, UpdateCheckResponse>> Check(string sourceId, CancellationToken cancellationToken) =>
        GetAsync<UpdateCheckResponse>($"/v1/check?source={Uri.EscapeDataString(sourceId)}", cancellationToken);

    public Task<Either<BaseError, UpdateStatusResponse>> GetStatus(CancellationToken cancellationToken) =>
        GetAsync<UpdateStatusResponse>("/v1/status", cancellationToken);

    public async Task<Either<BaseError, string>> GetLog(CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = CreateClient();
            using HttpResponseMessage response = await client.GetAsync("/v1/log", cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return BaseError.New($"Update agent log failed ({(int)response.StatusCode}): {body}");
            }

            return body;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Update agent log request failed");
            return BaseError.New(ex.Message);
        }
    }

    public async Task<Either<BaseError, UpdateStartResponse>> StartUpdate(
        string sourceId,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = CreateClient();
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "/v1/update",
                new { source = sourceId },
                JsonOptions,
                cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return BaseError.New($"Update start failed ({(int)response.StatusCode}): {body}");
            }

            UpdateStartResponse parsed = JsonSerializer.Deserialize<UpdateStartResponse>(body, JsonOptions);
            return parsed is null
                ? BaseError.New("Update agent returned empty start response")
                : parsed;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Update agent start request failed");
            return BaseError.New(ex.Message);
        }
    }

    private async Task<Either<BaseError, T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = CreateClient();
            using HttpResponseMessage response = await client.GetAsync(path, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return BaseError.New($"Update agent request failed ({(int)response.StatusCode}): {body}");
            }

            T parsed = JsonSerializer.Deserialize<T>(body, JsonOptions);
            return parsed is null
                ? BaseError.New("Update agent returned empty response")
                : parsed;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Update agent request to {Path} failed", path);
            return BaseError.New(ex.Message);
        }
    }

    private HttpClient CreateClient()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Update agent is not configured (ETV_UPDATE_AGENT_URL / ETV_UPDATE_AGENT_TOKEN)");
        }

        HttpClient client = httpClientFactory.CreateClient("UpdateAgent");
        client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("ETV_UPDATE_AGENT_URL")!.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Remove("X-Update-Token");
        client.DefaultRequestHeaders.Add(
            "X-Update-Token",
            Environment.GetEnvironmentVariable("ETV_UPDATE_AGENT_TOKEN"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }
}

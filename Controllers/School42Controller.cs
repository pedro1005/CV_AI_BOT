using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using CvAssistantWeb.Options;

namespace CvAssistantWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class School42Controller : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly School42Options _options;
        private readonly IMemoryCache _cache;
        private readonly ILogger<School42Controller> _logger;

        private const string TokenUrl = "https://api.intra.42.fr/oauth/token";

        public School42Controller(
            IHttpClientFactory httpClientFactory,
            IOptions<School42Options> options,
            IMemoryCache cache,
            ILogger<School42Controller> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _cache = cache;
            _logger = logger;
        }

        [HttpGet("Profile/{login}")]
        public async Task<IActionResult> GetProfile(string login)
        {
            try
            {
                _logger.LogInformation("Fetching 42 profile for user: {Login}", login);

                var client = _httpClientFactory.CreateClient();

                // 1️⃣ Get or reuse cached access token
                var accessToken = await GetAccessTokenAsync(client);
                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogWarning("Access token could not be obtained.");
                    return BadRequest("Failed to obtain access token from 42 API.");
                }

                // 2️⃣ Fetch user profile
                var apiUrl = $"https://api.intra.42.fr/v2/users/{login}";
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await client.GetAsync(apiUrl);
                var profileJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Error fetching profile for {Login}: {StatusCode} - {Body}", login, response.StatusCode, profileJson);
                    return BadRequest(new
                    {
                        error = "ProfileFetchError",
                        status = response.StatusCode,
                        details = profileJson
                    });
                }

                _logger.LogInformation("Profile successfully retrieved for {Login}", login);
                return Content(profileJson, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetProfile for login {Login}", login);
                return StatusCode(500, new { error = "InternalError", message = ex.Message });
            }
        }

        private async Task<string?> GetAccessTokenAsync(HttpClient client)
        {
            // Reuse cached token if available
            if (_cache.TryGetValue("42_access_token", out string cachedToken))
                return cachedToken;

            _logger.LogInformation("Fetching new 42 access token...");

            using var tokenRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret)
            });

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "CvAssistantWeb/1.0 (+https://yourdomain.com)"
            );
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var response = await client.PostAsync(TokenUrl, tokenRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token request failed: {Content}", content);
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (!root.TryGetProperty("access_token", out var tokenElement) || string.IsNullOrEmpty(tokenElement.GetString()))
                {
                    _logger.LogError("Access token not found in response: {Content}", content);
                    return null;
                }

                var accessToken = tokenElement.GetString()!;
                var expiresIn = root.TryGetProperty("expires_in", out var expiresElement) ? expiresElement.GetInt32() : 3600;

                // Cache token slightly before it expires
                var cacheDuration = TimeSpan.FromSeconds(Math.Max(expiresIn - 30, 30));
                _cache.Set("42_access_token", accessToken, cacheDuration);

                _logger.LogInformation("Access token cached for {Seconds}s", cacheDuration.TotalSeconds);
                return accessToken;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse token JSON: {Content}", content);
                return null;
            }
        }
        // GET /School42/EgressIp
[HttpGet("EgressIp")]
public async Task<IActionResult> GetEgressIp()
{
    try
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CvAssistantWeb-Diagnostic/1.0");


        // Use a simple egress IP service
        var resp = await client.GetAsync("https://ifconfig.co/json");
        var body = await resp.Content.ReadAsStringAsync();
        _logger.LogInformation("Egress IP check status: {Status}", resp.StatusCode);
        _logger.LogInformation("Egress IP response headers: {Headers}", string.Join(" | ", resp.Headers.Select(h => $"{h.Key}:{string.Join(',', h.Value)}")));
        // Return the JSON so you can see IP in your browser
        return Content(body, "application/json");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get egress IP");
        return StatusCode(500, new { error = "EgressIpError", message = ex.Message });
    }
}

// GET /School42/DiagnoseProfile/{login}
[HttpGet("DiagnoseProfile/{login}")]
public async Task<IActionResult> DiagnoseProfile(string login)
{
    try
    {
        var client = _httpClientFactory.CreateClient();

        // Get token but do NOT log secrets
        var accessToken = await GetAccessTokenAsync(client);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("Access token could not be obtained (diagnostics).");
            return BadRequest("Failed to obtain access token from 42 API.");
        }

        var apiUrl = $"https://api.intra.42.fr/v2/users/{login}";
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd("CvAssistantWeb-Diagnostic/1.0");

        request.Headers.Accept.ParseAdd("application/json");

        // Send and capture response
        var response = await client.SendAsync(request);
        var responseHeaders = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(", ", h.Value));

        var body = await response.Content.ReadAsStringAsync();
        var truncatedBody = body.Length > 2000 ? body.Substring(0, 2000) + "..." : body;

        // Log headers and truncated body (do NOT log token or client_secret)
        _logger.LogInformation("Diagnosis: Request to {Url} returned {Status}", apiUrl, response.StatusCode);
        foreach (var h in responseHeaders)
            _logger.LogInformation("DiagHeader: {Key} = {Value}", h.Key, h.Value);

        // If Cloudflare headers exist, log them explicitly for visibility
        if (responseHeaders.TryGetValue("cf-ray", out var cfRay))
            _logger.LogInformation("Found cf-ray: {CfRay}", cfRay);
        if (responseHeaders.TryGetValue("server", out var server))
            _logger.LogInformation("Found server header: {Server}", server);

        // Return diagnostic info to caller (safe — no secrets)
        return Ok(new
        {
            status = response.StatusCode,
            headers = responseHeaders,
            body = truncatedBody
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error in DiagnoseProfile for login {Login}", login);
        return StatusCode(500, new { error = "InternalError", message = ex.Message });
    }
}

    }
}


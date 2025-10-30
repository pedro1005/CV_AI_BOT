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

        // =========================
        // 1️⃣ Check if ClientId is loaded
        // =========================
        [HttpGet("CheckOptions")]
        public IActionResult CheckOptions()
        {
            _logger.LogInformation("School42 ClientId: {ClientId}", _options.ClientId);
            return Ok(new
            {
                clientId = !string.IsNullOrWhiteSpace(_options.ClientId) ? "<set>" : "<missing>"
            });
        }

        // =========================
        // 2️⃣ Egress IP diagnostic
        // =========================
        [HttpGet("EgressIp")]
        public async Task<IActionResult> GetEgressIp()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CvAssistantWeb", "1.0"));
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(Diagnostic)"));

                var resp = await client.GetAsync("https://ifconfig.co/json");
                var body = await resp.Content.ReadAsStringAsync();

                _logger.LogInformation("Egress IP response headers: {Headers}", string.Join(" | ", resp.Headers.Select(h => $"{h.Key}:{string.Join(',', h.Value)}")));

                return Content(body, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get egress IP");
                return StatusCode(500, new { error = "EgressIpError", message = ex.Message });
            }
        }

        // =========================
        // 3️⃣ Diagnose 42 profile
        // =========================
        [HttpGet("DiagnoseProfile/{login}")]
        public async Task<IActionResult> DiagnoseProfile(string login)
        {
            var client = _httpClientFactory.CreateClient();

            try
            {
                // Get or fetch access token
                var accessToken = await GetAccessTokenAsync(client);
                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogWarning("Access token could not be obtained (diagnostics).");
                    return await ReturnFallbackProfileAsync(login, "Failed to obtain access token from 42 API.");
                }

                var apiUrl = $"https://api.intra.42.fr/v2/users/{login}";
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CvAssistantWeb", "1.0"));
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(Diagnostic)"));
                request.Headers.Accept.ParseAdd("application/json");

                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                // If request failed → use fallback JSON
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("42 API call failed with {Status}. Falling back to local JSON.", response.StatusCode);
                    return await ReturnFallbackProfileAsync(login, $"42 API returned {response.StatusCode}");
                }

                // Log diagnostic headers
                var responseHeaders = response.Headers.Concat(response.Content.Headers)
                    .ToDictionary(h => h.Key, h => string.Join(", ", h.Value));

                var truncatedBody = body.Length > 2000 ? body.Substring(0, 2000) + "..." : body;

                // Log Cloudflare info if present
                if (responseHeaders.TryGetValue("cf-ray", out var cfRay))
                    _logger.LogInformation("Found cf-ray: {CfRay}", cfRay);
                if (responseHeaders.TryGetValue("server", out var server))
                    _logger.LogInformation("Found server header: {Server}", server);

                return Ok(new
                {
                    status = response.StatusCode,
                    headers = responseHeaders,
                    body = truncatedBody
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching 42 profile for {Login}", login);
                return await ReturnFallbackProfileAsync(login, ex.Message);
            }
        }

        // =========================
        // 4️⃣ Access token helper
        // =========================
        private async Task<string?> GetAccessTokenAsync(HttpClient client)
        {
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
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CvAssistantWeb", "1.0"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(TokenRequest)"));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var response = await client.PostAsync(TokenUrl, tokenRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token request failed. StatusCode: {StatusCode}, ResponseBody: {Content}",
                                 response.StatusCode, content);
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

        // =========================
        // 5️⃣ Fallback profile helper
        // =========================
        private async Task<IActionResult> ReturnFallbackProfileAsync(string login, string reason)
        {
            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "Data", $"{login}.json");

                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("Fallback file not found for login {Login}. Path: {Path}", login, filePath);
                    return NotFound(new { error = "ProfileNotFound", message = $"No fallback data for '{login}'." });
                }

                var json = await System.IO.File.ReadAllTextAsync(filePath);
                _logger.LogInformation("Loaded fallback profile for {Login} (reason: {Reason})", login, reason);

                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load fallback profile for {Login}", login);
                return StatusCode(500, new { error = "FallbackError", message = ex.Message });
            }
        }

        // =========================
        // 6️⃣ Test token endpoint
        // =========================
        [HttpGet("TestToken")]
        public async Task<IActionResult> TestToken()
        {
            var client = _httpClientFactory.CreateClient();
            var token = await GetAccessTokenAsync(client);
            if (string.IsNullOrEmpty(token))
                return BadRequest("Token fetch failed; check logs.");
            return Ok(new { token = "<redacted>" });
        }
    }
}

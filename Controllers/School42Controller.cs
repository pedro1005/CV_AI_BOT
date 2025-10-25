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

                var client = _httpClientFactory.CreateClient("School42");

                // 1️⃣ Get (or reuse) cached token
                var accessToken = await GetAccessTokenAsync(client);
                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogWarning("Access token could not be obtained.");
                    return BadRequest("Failed to obtain access token from 42 API.");
                }

                // 2️⃣ Call the 42 API
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
            if (_cache.TryGetValue("42_access_token", out string cachedToken))
                return cachedToken;

            _logger.LogInformation("Fetching new 42 access token...");

            // Prepare the request content
            var tokenRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret)
            });

            // 🧠 Cloudflare mitigation headers
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Connection.Add("keep-alive");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

            try
            {
                var response = await client.PostAsync(TokenUrl, tokenRequest);
                var content = await response.Content.ReadAsStringAsync();

                // 🪲 Cloudflare detection
                if ((int)response.StatusCode == 403 && content.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Cloudflare blocked the OAuth token request. Response: {HtmlSnippet}", content[..Math.Min(400, content.Length)]);
                    _logger.LogError("If this persists, contact 42 support to whitelist your server IP.");
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Token request failed with {Status}: {Body}", response.StatusCode, content);
                    return null;
                }

                using var doc = JsonDocument.Parse(content);
                var accessToken = doc.RootElement.GetProperty("access_token").GetString();
                var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

                // Cache token with safety margin
                _cache.Set("42_access_token", accessToken, TimeSpan.FromSeconds(expiresIn - 60));
                _logger.LogInformation("Access token cached for {Seconds}s", expiresIn - 60);

                return accessToken;
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "HTTP error while requesting 42 OAuth token.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during token request.");
                return null;
            }
        }
    }
}

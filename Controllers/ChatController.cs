using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using CvAssistantWeb.Models;
using CvAssistantWeb.Data; // <- namespace do seu DbContext
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace CvAssistantWeb.Controllers
{
    public class ChatController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _cvJson;
        private readonly AppDbContext _db;

        public ChatController(IHttpClientFactory httpClientFactory, IWebHostEnvironment env, AppDbContext db)
        {
            _httpClientFactory = httpClientFactory;
            _db = db;

            var cvPath = Path.Combine(env.WebRootPath, "data", "cv.json");
            _cvJson = System.IO.File.Exists(cvPath)
                ? System.IO.File.ReadAllText(cvPath)
                : "{}";
        }

        public IActionResult Index()
        {
            return View();
        }

        // ✅ Chat endpoint (IA)
        [HttpPost]
        public async Task<IActionResult> Ask(string userMessage)
        {
            var client = _httpClientFactory.CreateClient("CometAPI");

            var payload = new
            {
                model = "gpt-3.5-turbo",
                messages = new object[]
                {
                    new { role = "system", content = $"You are a junior software developer looking for a job with CV {_cvJson}. Answer in plain text." },
                    new { role = "user", content = userMessage }
                }
            };

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("chat/completions", content);
                response.EnsureSuccessStatusCode();

                var resultJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resultJson);

                var reply = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return Json(new { reply });
            }
            catch (Exception ex)
            {
                return Json(new { reply = "Error: " + ex.Message });
            }
        }

        // ✅ Send message to DB (PostgreSQL)
        [HttpPost]
        public IActionResult SendMessage(string userMessage)
        {
            try
            {
                // Example input:
                // "Send a message to Company: TechCorp, Contact: Jane Doe, Message: Hello"
                string company = ExtractBetween(userMessage, "Company:", ", Contact:");
                string contact = ExtractBetween(userMessage, "Contact:", ", Message:");
                string message = ExtractAfter(userMessage, "Message:");

                if (string.IsNullOrWhiteSpace(company) ||
                    string.IsNullOrWhiteSpace(contact) ||
                    string.IsNullOrWhiteSpace(message))
                {
                    return Json(new { reply = "Could not parse the message. Include Company, Contact, and Message." });
                }

                var newMessage = new ContactMessage
                {
                    Company = company.Trim(),
                    Contact = contact.Trim(),
                    Message = message.Trim(),
                    Date = DateTime.UtcNow
                };

                // ✅ Save to PostgreSQL
                _db.Messages.Add(newMessage);
                _db.SaveChanges();

                return Json(new { reply = $"Message saved successfully for {contact} at {company}!" });
            }
            catch (Exception ex)
            {
                return Json(new { reply = "Error: " + ex.Message });
            }
        }
        
        [HttpPost]
        public IActionResult GetAllMessages(string password)
        {
            var correctPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

            if (string.IsNullOrEmpty(correctPassword))
                return Json(new { success = false, reply = "Server misconfigured: ADMIN_PASSWORD not set." });

            if (password != correctPassword)
                return Json(new { success = false, reply = "Invalid password!" });

            var messages = _db.Messages
                .OrderByDescending(m => m.Date)
                .Select(m => new 
                {
                    Company = m.Company,
                    Contact = m.Contact,
                    Message = m.Message,
                    Date = m.Date.ToString("yyyy-MM-dd HH:mm:ss") // always UTC
                })
                .ToList();

            return Json(new { success = true, messages });
        }



        // 🔧 Helpers
        private string ExtractBetween(string text, string start, string end)
        {
            int startIndex = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
            int endIndex = text.IndexOf(end, StringComparison.OrdinalIgnoreCase);
            if (startIndex == -1 || endIndex == -1 || endIndex <= startIndex)
                return "";
            return text.Substring(startIndex + start.Length, endIndex - (startIndex + start.Length)).Trim();
        }

        private string ExtractAfter(string text, string start)
        {
            int startIndex = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
            if (startIndex == -1) return "";
            return text[(startIndex + start.Length)..].Trim();
        }
    }
    
   public class School42Controller : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        //private readonly string _clientId = "CLI_ID";     
        //private readonly string _clientSecret = "CLI_SECRET"; 
        private readonly string _clientId = "u-s4t2ud-378086f35e7f071a05d08033df7b1df2f4f88adfb13773c01bb9af0fc097900f";
        private readonly string _clientSecret = "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";

        private readonly string _tokenUrl = "https://api.intra.42.fr/oauth/token";
        private readonly string _apiUrl = "https://api.intra.42.fr/v2/users/pedmonte"; // <-- login fixo

        public School42Controller(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("/School42/Profile")]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();

                // 1️⃣ Obter access_token via Client Credentials
                var tokenRequest = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("grant_type","client_credentials"),
                    new KeyValuePair<string,string>("client_id", _clientId),
                    new KeyValuePair<string,string>("client_secret", _clientSecret)
                });

                var tokenResponse = await client.PostAsync(_tokenUrl, tokenRequest);
                if (!tokenResponse.IsSuccessStatusCode)
                    return BadRequest("Erro ao obter token da API 42.");

                var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                using var tokenDoc = JsonDocument.Parse(tokenJson);
                var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

                if (string.IsNullOrEmpty(accessToken))
                    return BadRequest("Token da API 42 é nulo ou inválido.");

                // 2️⃣ Chamar a API da 42 com login fixo
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await client.GetAsync(_apiUrl);

                if (!response.IsSuccessStatusCode)
                    return BadRequest("Erro ao conectar com a API da 42.");

                var profileJson = await response.Content.ReadAsStringAsync();

                // Retorna JSON diretamente para o JS processar
                return Content(profileJson, "application/json");
            }
            catch (Exception ex)
            {
                return BadRequest($"Erro interno: {ex.Message}");
            }
        }
    }
}

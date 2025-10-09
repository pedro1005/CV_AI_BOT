using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using CvAssistantWeb.Models;

public class ChatController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _cvJson;

    public ChatController(IHttpClientFactory httpClientFactory, IWebHostEnvironment env)
    {
        _httpClientFactory = httpClientFactory;
        var cvPath = Path.Combine(env.WebRootPath, "data", "cv.json");
        _cvJson = System.IO.File.ReadAllText(cvPath);
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Ask(string userMessage)
    {
        //var client = _httpClientFactory.CreateClient("OpenRouter");
        var client = _httpClientFactory.CreateClient();
        var apiKey = "sk-4TNPT9S407B6ZXhsPZGfd9gYuzv070wD5gy8zc9bnnHyYVc7";
        client.BaseAddress = new Uri("https://api.cometapi.com/v1/");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = "gpt-4o",
            messages = new object[]
            {
                new { role = "system", content = $"You are a software developer junior looking for a job as junior or intern with cv {_cvJson}. Will answear in plain text, never like json format." },
                new { role = "user", content = $".Question: {userMessage}. if question info not available in cv answer 'Sorry, info not available.'" }
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
    
    [HttpPost]
public IActionResult SendMessage(string userMessage)
{
    try
    {
        // Expecting user input like:
        // "Send a message to Company: TechCorp, Contact: Jane Doe, Message: Hello"
        string company = ExtractBetween(userMessage, "Company:", ", Contact:");
        string contact = ExtractBetween(userMessage, "Contact:", ", Message:");
        string message = ExtractAfter(userMessage, "Message:");

        if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(contact) || string.IsNullOrWhiteSpace(message))
        {
            return Json(new { reply = "Could not parse the message. Include Company, Contact, and Message." });
        }

        var newMessage = new ContactMessage
        {
            Company = company.Trim(),
            Contact = contact.Trim(),
            Message = message.Trim(),
            Date = DateTime.Now
        };

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "data", "messages.json");
        List<ContactMessage> allMessages = new();

        if (System.IO.File.Exists(filePath))
        {
            var existingJson = System.IO.File.ReadAllText(filePath);
            if (!string.IsNullOrWhiteSpace(existingJson))
                allMessages = JsonSerializer.Deserialize<List<ContactMessage>>(existingJson) ?? new();
        }

        allMessages.Add(newMessage);
        System.IO.File.WriteAllText(filePath, JsonSerializer.Serialize(allMessages, new JsonSerializerOptions { WriteIndented = true }));

        return Json(new { reply = "Message saved successfully!" });
    }
    catch (Exception ex)
    {
        return Json(new { reply = "Error: " + ex.Message });
    }
}

// Helpers
private string ExtractBetween(string text, string start, string end)
{
    int startIndex = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
    int endIndex = text.IndexOf(end, StringComparison.OrdinalIgnoreCase);
    if (startIndex == -1 || endIndex == -1 || endIndex <= startIndex) return "";
    return text.Substring(startIndex + start.Length, endIndex - startIndex - start.Length);
}

private string ExtractAfter(string text, string start)
{
    int startIndex = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
    if (startIndex == -1) return "";
    return text.Substring(startIndex + start.Length);
}

}
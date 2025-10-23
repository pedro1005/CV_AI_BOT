using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using System;
using CvAssistantWeb.Data;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using CvAssistantWeb.Options;

var builder = WebApplication.CreateBuilder(args);

// Add MVC controllers and views
builder.Services.AddControllersWithViews();


// =======================
// 🔹 DATABASE CONFIG
// =======================
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
Console.WriteLine($"DATABASE_URL: '{databaseUrl}'");

string connectionString;

if (!string.IsNullOrEmpty(databaseUrl))
{
    // Normalize URI for Postgres compatibility
    if (databaseUrl.StartsWith("postgresql://"))
        databaseUrl = databaseUrl.Replace("postgresql://", "postgres://");

    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':');
    var port = uri.Port > 0 ? uri.Port : 5432;

    connectionString =
        $"Host={uri.Host};Port={port};Username={userInfo[0]};Password={userInfo[1]};" +
        $"Database={uri.LocalPath.TrimStart('/')};SSL Mode=Require;Trust Server Certificate=true";

    Console.WriteLine($"Connecting to {uri.Host}:{port}, database: {uri.LocalPath.TrimStart('/')}");
}
else
{
    Console.WriteLine("⚠️ No DATABASE_URL found — using local connection string.");
    connectionString = "Host=localhost;Database=cvassistant;Username=postgres;Password=1234;SSL Mode=Disable";
}

// Register DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));


// =======================
// 🔹 COMET API CLIENT
// =======================
var cometApiKey = Environment.GetEnvironmentVariable("COMET_API_KEY");
builder.Services.AddHttpClient("CometAPI", client =>
{
    client.BaseAddress = new Uri("https://api.cometapi.com/v1/");
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cometApiKey);
    client.DefaultRequestHeaders.Add("X-Title", "CV Assistant");
});


// =======================
// 🔹 42 API CONFIG + CLIENT
// =======================
// Bind the "School42" section from appsettings.json to a strongly typed class
builder.Services.Configure<School42Options>(
    builder.Configuration.GetSection("School42")
);

// Add memory cache for token reuse
builder.Services.AddMemoryCache();

// Register typed HttpClient for 42 API
builder.Services.AddHttpClient("School42", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Pedro-CV-App/1.0");
});


// =======================
// 🔹 PIPELINE CONFIG
// =======================
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Default MVC route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Chat}/{action=Index}/{id?}"
);

// ✅ Run migrations automatically on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using CvAssistantWeb.Data;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
Console.WriteLine($"DATABASE_URL: '{databaseUrl}'");


string connectionString;

if (!string.IsNullOrEmpty(databaseUrl))
{
    // Normalize scheme for compatibility
    if (databaseUrl.StartsWith("postgresql://"))
        databaseUrl = databaseUrl.Replace("postgresql://", "postgres://");

    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':');

    // Use default PostgreSQL port 5432 if port is missing
    var port = uri.Port > 0 ? uri.Port : 5432;

    connectionString = $"Host={uri.Host};Port={port};Username={userInfo[0]};Password={userInfo[1]};Database={uri.LocalPath.TrimStart('/')};SSL Mode=Require;Trust Server Certificate=true";

    // Optional debug
    Console.WriteLine($"Connecting to {uri.Host}:{port}, database: {uri.LocalPath.TrimStart('/')}");
}
else
{
    Console.WriteLine("Error: No connection string configured!!!!!!!!!!!!!!!!!!!!!");
    // fallback local
    connectionString = "Host=localhost;Database=cvassistant;Username=postgres;Password=1234;SSL Mode=Disable";
}


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));


var cometApiKey = Environment.GetEnvironmentVariable("COMET_API_KEY");
// Registrar HttpClient para OpenRouter
builder.Services.AddHttpClient("CometAPI", client =>
{
    client.BaseAddress = new Uri("https://api.cometapi.com/v1/");
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cometApiKey); // chave direta

    client.DefaultRequestHeaders.Add("X-Title", "CV Assistant");
});


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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Chat}/{action=Index}/{id?}");

// ✅ Run migrations automatically on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
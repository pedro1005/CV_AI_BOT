using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

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

app.Run();
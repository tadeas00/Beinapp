using System;
using EduApp.Test3.Web.Components;
using EduApp.Test3.Shared.Services;
using EduApp.Test3.Web.Services;
using Resend; // 1. Přidat tento using
using Npgsql; // Pro databázi
using Dapper; // Pro jednodušší dotazy

var builder = WebApplication.CreateBuilder(args);

// --- SEKCE SLUŽEB (DI Container) ---

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddScoped<UserState>();
builder.Services.AddSingleton<EduApp.Test3.Shared.Services.ExamRepository>();

// 2. Registrace Resend služby
var resendApiKey = builder.Configuration["Resend:ApiKey"] 
                   ?? Environment.GetEnvironmentVariable("Resend__ApiKey") 
                   ?? ""; 

builder.Services.AddOptions();
builder.Services.AddHttpClient<IResend, ResendClient>();
builder.Services.Configure<ResendClientOptions>(options =>
{
    options.ApiToken = resendApiKey;
});
builder.Services.AddTransient<IResend, ResendClient>();

// 3. Registrace tvé Emailové služby
builder.Services.AddTransient<IEmailService, EmailService>();

// 4. Registrace DB spojení (předpokládám, že máš v Secrets ConnectionString)
builder.Services.AddScoped(sp => 
    new NpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")));


var app = builder.Build();

// --- SEKCE MIDDLEWARE A ENDPOINTŮ ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

// 5. Tvůj nový registrační endpoint
app.MapPost("/api/register", async (RegisterRequest model, NpgsqlConnection db, IEmailService emailService) =>
{
    // Uložení do DB
    const string sql = "INSERT INTO public.users (email, username) VALUES (@Email, @Username)";
    await db.ExecuteAsync(sql, new { model.Email, model.Username });

    // Odeslání mailu
    await emailService.SendRegistrationEmailAsync(model.Email, model.Username);

    return Results.Ok("Registrace a mail OK!");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(EduApp.Test3.Shared._Imports).Assembly);

app.Run();

// Jednoduchý model pro endpoint
public record RegisterRequest(string Email, string Username);
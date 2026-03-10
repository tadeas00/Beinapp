using System;
using EduApp.Test3.Web.Components;
using EduApp.Test3.Shared.Services;
using EduApp.Test3.Web.Services;
using Resend; 
using Npgsql; 
using Dapper; 

var builder = WebApplication.CreateBuilder(args);

// --- SEKCE SLUŽEB (DI Container) ---

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddScoped<UserState>();
builder.Services.AddSingleton<ExamRepository>();

// 2. Registrace Resend služby (Opraveno pro správnou funkčnost)
var resendApiKey = builder.Configuration["Resend:ApiKey"] 
                   ?? Environment.GetEnvironmentVariable("Resend__ApiKey") 
                   ?? ""; 

builder.Services.AddOptions();
builder.Services.Configure<ResendClientOptions>(options =>
{
    options.ApiToken = resendApiKey;
});
// AddHttpClient automaticky zaregistruje IResend, není potřeba přidávat AddTransient
builder.Services.AddHttpClient<IResend, ResendClient>();

// 3. Registrace tvé Emailové služby
builder.Services.AddTransient<IEmailService, EmailService>();

// 4. Registrace DB spojení (Sladěno s appsettings.json -> "MyDb")
builder.Services.AddScoped(sp => 
    new NpgsqlConnection(builder.Configuration.GetConnectionString("MyDb") 
                         ?? Environment.GetEnvironmentVariable("ConnectionStrings__MyDb")));

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

// Zastaralý a chybový endpoint /api/register byl smazán. 
// Registraci nyní plně a bezpečně řeší přímo komponenta Register.razor!

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
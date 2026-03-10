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

// Registrace Resend služby
var resendApiKey = builder.Configuration["Resend:ApiKey"] 
                   ?? Environment.GetEnvironmentVariable("Resend__ApiKey") 
                   ?? ""; 

builder.Services.AddOptions();
builder.Services.Configure<ResendClientOptions>(options =>
{
    options.ApiToken = resendApiKey;
});
builder.Services.AddHttpClient<IResend, ResendClient>();

// Registrace tvé Emailové služby
builder.Services.AddTransient<IEmailService, EmailService>();

// Registrace DB spojení
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

// TADY BYLA CHYBA: Chybělo napojení na sdílenou knihovnu (Shared), kde máš stránky!
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ExamRepository).Assembly);

app.Run();
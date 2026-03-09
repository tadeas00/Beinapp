using Microsoft.Extensions.Logging;
using EduApp.Test3.Shared.Services;
using EduApp.Test3.Services;

namespace EduApp.Test3;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        // Add device-specific services used by the EduApp.Test3.Shared project
        builder.Services.AddSingleton<IFormFactor, FormFactor>();

        builder.Services.AddMauiBlazorWebView();
        
        builder.Services.AddScoped<UserState>();
        builder.Services.AddSingleton<EduApp.Test3.Shared.Services.ExamRepository>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
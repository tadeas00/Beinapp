using Resend;

namespace EduApp.Test3.Web.Services; 

public interface IEmailService
{
    // Přidán parametr pro hotový dynamický odkaz
    Task SendRegistrationEmailAsync(string targetEmail, string userName, string verificationCode, string magicLink);
}

public class EmailService(IResend resend) : IEmailService
{
    public async Task SendRegistrationEmailAsync(string targetEmail, string userName, string verificationCode, string magicLink)
    {
        var message = new EmailMessage();
        message.From = "EduApp <info@b3in.cz>"; // Tvoje doména
        message.To.Add(targetEmail);
        message.Subject = "Vítej v EduApp! 🚀 Tvůj ověřovací kód";
        
        message.HtmlBody = $@"
        <!DOCTYPE html>
        <html lang='cs'>
        <head>
            <meta charset='UTF-8'>
            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            <title>Ověřovací kód</title>
        </head>
        <body style='margin: 0; padding: 0; background-color: #f8fafc; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;'>
            <table width='100%' border='0' cellspacing='0' cellpadding='0' style='background-color: #f8fafc; padding: 40px 20px;'>
                <tr>
                    <td align='center'>
                        <table width='100%' max-width='500' border='0' cellspacing='0' cellpadding='0' style='max-width: 500px; background-color: #ffffff; border-radius: 24px; padding: 40px; box-shadow: 0 4px 6px rgba(0,0,0,0.05);'>
                            <tr>
                                <td align='center'>
                                    <div style='width: 56px; height: 56px; background: linear-gradient(135deg, #6366f1 0%, #a855f7 100%); border-radius: 16px; text-align: center; line-height: 56px; font-size: 28px; margin-bottom: 24px;'>
                                        🚀
                                    </div>
                                    <h1 style='color: #0f172a; font-size: 24px; font-weight: 800; margin: 0 0 12px 0;'>Vítej v EduApp!</h1>
                                    <p style='color: #64748b; font-size: 16px; line-height: 1.6; margin: 0 0 32px 0;'>
                                        Ahoj <strong>{userName}</strong>,<br><br>
                                        jsme nadšeni, že ses k nám přidal! Pro dokončení registrace klikni na tlačítko níže, nebo zadej kód přímo do aplikace:
                                    </p>
                                    
                                    <div style='background-color: #f1f5f9; border-radius: 16px; padding: 24px; margin-bottom: 24px; text-align: center;'>
                                        <span style='font-family: monospace; font-size: 42px; font-weight: 800; letter-spacing: 12px; color: #4f46e5;'>{verificationCode}</span>
                                    </div>

                                    <div style='margin-bottom: 32px; text-align: center;'>
                                        <a href='{magicLink}' 
                                           style='display: inline-block; background-color: #4f46e5; color: #ffffff; font-weight: bold; font-size: 14px; text-decoration: none; padding: 16px 32px; border-radius: 12px; text-transform: uppercase; letter-spacing: 1px; box-shadow: 0 4px 15px rgba(79, 70, 229, 0.3);'>
                                            Ověřit e-mail automaticky
                                        </a>
                                    </div>
                                    
                                    <p style='color: #94a3b8; font-size: 13px; line-height: 1.5; margin: 0;'>
                                        Pokud jsi o registraci nežádal, můžeš tento e-mail s klidem ignorovat.
                                    </p>
                                </td>
                            </tr>
                        </table>
                    </td>
                </tr>
            </table>
        </body>
        </html>";

        await resend.EmailSendAsync(message);
    }
}
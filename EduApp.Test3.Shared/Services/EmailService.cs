using Resend;

namespace EduApp.Test3.Web.Services; // Uprav podle své jmenné konvence

public interface IEmailService
{
    Task SendRegistrationEmailAsync(string targetEmail, string userName);
}

public class EmailService(IResend resend) : IEmailService
{
    public async Task SendRegistrationEmailAsync(string targetEmail, string userName)
    {
        var message = new EmailMessage();
        message.From = "EduApp <info@b3in.cz>"; // Tady dej svou doménu z Resendu!
        message.To.Add(targetEmail);
        message.Subject = "Vítej v EduApp! 🚀";
        message.HtmlBody = $"<h1>Ahoj {userName}!</h1><p>Díky za registraci.</p>";

        await resend.EmailSendAsync(message);
    }
}
namespace OnlineDisscussionForum.Services;
public interface IEmailSender
{
    bool IsEnabled { get; }
    Task SendEmailAsync(string email, string subject, string message);
}

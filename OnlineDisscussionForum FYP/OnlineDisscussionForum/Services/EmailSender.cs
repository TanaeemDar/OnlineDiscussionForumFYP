using System.Text.Json;
namespace OnlineDisscussionForum.Services;

// Production email is intentionally disabled. Development pickup files are never publicly served.
public class EmailSender(IConfiguration configuration, LocalDataPaths paths, IWebHostEnvironment environment) : IEmailSender
{
    public bool IsEnabled => environment.IsDevelopment() && configuration["Email:Mode"] == "Pickup";
    public async Task SendEmailAsync(string email, string subject, string message)
    {
        if (!IsEnabled) throw new InvalidOperationException("Email delivery is disabled.");
        var directory = Path.Combine(paths.Root, "mail"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { To = email, Subject = subject, HtmlBody = message }));
    }
}

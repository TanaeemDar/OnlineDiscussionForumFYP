using Microsoft.EntityFrameworkCore;
using OnlineDisscussionForum.Data;

namespace OnlineDisscussionForum;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        using (var scope = host.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (args.Contains("--migrate"))
            {
                await context.Database.MigrateAsync();
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedSuperSeeder();
                return;
            }
            if ((await context.Database.GetPendingMigrationsAsync()).Any())
                throw new InvalidOperationException("Database migration required. Run the application with --migrate before starting it.");
        }
        await host.RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(web => web.UseStartup<Startup>());
}

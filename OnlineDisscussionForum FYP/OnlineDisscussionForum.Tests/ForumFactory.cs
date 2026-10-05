using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnlineDisscussionForum;
using OnlineDisscussionForum.Data;

namespace OnlineDisscussionForum.Tests;

public class ForumFactory : WebApplicationFactory<Program>
{
    public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "forum-tests-" + Guid.NewGuid());
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["DataDirectory"] = DataPath,
            ["Admin:UserName"] = "TestAdmin", ["Admin:Email"] = "admin@example.test", ["Admin:Password"] = "TestAdmin!23456",
            ["Email:Mode"] = "Pickup", ["Logging:LogLevel:Default"] = "Warning", ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning"
        }));
    }
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedSuperSeeder().GetAwaiter().GetResult();
        host.Start();
        return host;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(DataPath)) Directory.Delete(DataPath, true);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OnlineDisscussionForum.Data;
namespace OnlineDisscussionForum.Services;
public class SqliteHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken token = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM AspNetUsers LIMIT 1;", token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("Database is unavailable."); }
    }
}

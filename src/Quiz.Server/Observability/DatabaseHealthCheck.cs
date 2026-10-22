using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quiz.Server.Data;

namespace Quiz.Server.Observability;

/// <summary>Healthy when the SQLite file can be opened and queried.</summary>
public sealed class DatabaseHealthCheck(IDbContextFactory<QuizDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database cannot be opened.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The database cannot be opened.", ex);
        }
    }
}

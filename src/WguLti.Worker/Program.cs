using WguLti.Core.Stores;
using WguLti.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWguLtiInfrastructure(builder.Configuration);
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<PurgeWorker>();
builder.Services.AddHostedService<AuditPartitionWorker>();

var host = builder.Build();
host.Run();

sealed class OutboxWorker(IServiceScopeFactory scopes, ILogger<OutboxWorker> logger) : BackgroundService
{
    private readonly string _id = Environment.MachineName + ":" + Environment.ProcessId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox worker {WorkerId} started.", _id);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundMaintenance>();
                await jobs.DispatchOutboxOnceAsync(_id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch failed. The worker will retry.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

sealed class PurgeWorker(IServiceScopeFactory scopes, ILogger<PurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundMaintenance>();
                await jobs.PurgeExpiredAsync(stoppingToken);
                logger.LogInformation("purge_expired completed.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "purge_expired failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

sealed class AuditPartitionWorker(IServiceScopeFactory scopes, ILogger<AuditPartitionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundMaintenance>();
                var names = await jobs.EnsureAuditPartitionsAsync(stoppingToken);
                logger.LogInformation("Audit partitions ready: {Partitions}", string.Join(", ", names));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ensure_audit_partitions failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

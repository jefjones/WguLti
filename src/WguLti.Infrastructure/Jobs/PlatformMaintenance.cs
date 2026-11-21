using Microsoft.EntityFrameworkCore;
using Npgsql;
using WguLti.Core.Stores;
using WguLti.Infrastructure.Persistence;

namespace WguLti.Infrastructure.Jobs;

public sealed class PlatformMaintenance : IBackgroundMaintenance
{
    private readonly LtiDbContext _db;

    public PlatformMaintenance(LtiDbContext db) => _db = db;

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        await using var cmd = await CommandAsync("SELECT lti.purge_expired()", ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<string>> EnsureAuditPartitionsAsync(CancellationToken ct)
    {
        await using var cmd = await CommandAsync("SELECT lti.ensure_audit_partitions(3)", ct);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var names = new List<string>();
        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));
        return names;
    }

    public async Task<int> DispatchOutboxOnceAsync(string workerId, CancellationToken ct)
    {
        var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var claim = new NpgsqlCommand(
            """
            UPDATE lti.outbox_jobs SET
              locked_by = @worker,
              locked_until = now() + interval '2 minutes',
              attempts = attempts + 1
            WHERE id = (
              SELECT id FROM lti.outbox_jobs
              WHERE completed_at IS NULL
                AND run_after <= now()
                AND (locked_until IS NULL OR locked_until < now())
                AND attempts < max_attempts
              ORDER BY id
              FOR UPDATE SKIP LOCKED
              LIMIT 1
            )
            RETURNING id, kind, attempts, max_attempts
            """, conn, tx);
        claim.Parameters.AddWithValue("worker", workerId);
        await using var reader = await claim.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            await reader.DisposeAsync();
            await tx.CommitAsync(ct);
            return 0;
        }

        var id = reader.GetInt64(0);
        var kind = reader.GetString(1);
        var attempts = reader.GetInt32(2);
        var maxAttempts = reader.GetInt32(3);
        await reader.DisposeAsync();

        // TODO Phase 7: deliver PNS notices (LtiAssetProcessorSubmissionNotice) with retry.
        // Until a handler exists, unknown kinds are retried with backoff and then parked.
        var error = $"No handler for outbox kind '{kind}'.";
        var backoff = Math.Min(900, (int)Math.Pow(2, Math.Min(attempts, 10)));
        await using var finish = new NpgsqlCommand(
            """
            UPDATE lti.outbox_jobs SET
              last_error = @error,
              locked_until = NULL,
              completed_at = CASE WHEN attempts >= max_attempts THEN now() ELSE NULL END,
              run_after = CASE WHEN attempts >= max_attempts THEN run_after ELSE now() + make_interval(secs => @backoff) END
            WHERE id = @id
            """, conn, tx);
        finish.Parameters.AddWithValue("error", error);
        finish.Parameters.AddWithValue("backoff", backoff);
        finish.Parameters.AddWithValue("id", id);
        _ = maxAttempts;
        await finish.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return 1;
    }

    private async Task<NpgsqlCommand> CommandAsync(string sql, CancellationToken ct)
    {
        var conn = await OpenAsync(ct);
        return new NpgsqlCommand(sql, conn);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        return conn;
    }
}

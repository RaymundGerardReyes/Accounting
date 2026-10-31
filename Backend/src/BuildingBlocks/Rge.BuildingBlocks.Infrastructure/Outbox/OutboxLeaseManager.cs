namespace Rge.BuildingBlocks.Infrastructure.Outbox;



/// <summary>

/// Manages worker identity and lease durations for lock-free concurrent outbox polling

/// using PostgreSQL FOR UPDATE SKIP LOCKED.

/// </summary>

public sealed class OutboxLeaseManager

{

    public string WorkerId { get; }

    public TimeSpan LeaseDuration { get; }

    public int BatchSize { get; }



    public OutboxLeaseManager(

        string? workerId = null,

        TimeSpan? leaseDuration = null,

        int batchSize = 50)

    {

        WorkerId = string.IsNullOrWhiteSpace(workerId)

            ? $"{Environment.MachineName}-{Guid.NewGuid():N}"

            : workerId;

        LeaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30);

        BatchSize = Math.Clamp(batchSize, 1, 500);

    }

}

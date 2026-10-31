namespace Rge.BuildingBlocks.SharedKernel.Primitives;

/// <summary>
/// Represents a domain event captured during an aggregate state transition.
/// </summary>
public abstract record DomainEvent(Guid EventId, DateTimeOffset OccurredAtUtc)
{
    protected DomainEvent() : this(Guid.NewGuid(), DateTimeOffset.UtcNow)
    {
    }
}


using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.CostAllocation.Domain;

public sealed class CostActivity : Entity<string>
{
    public string ActivityName { get; }
    public Money OverheadPool { get; }
    public CostDriver Driver { get; }

    public CostActivity(
        string id,
        string activityName,
        Money overheadPool,
        CostDriver driver) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        ActivityName = Guard.AgainstNullOrWhiteSpace(activityName);
        OverheadPool = overheadPool;
        Driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }
}


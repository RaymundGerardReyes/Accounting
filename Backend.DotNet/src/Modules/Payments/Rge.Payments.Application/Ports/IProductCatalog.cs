namespace Rge.Payments.Application.Ports;

/// <summary>
/// Port for retrieving product catalog metadata and prices.
/// </summary>
public interface IProductCatalog
{
    Task<int?> GetUnitPricePesosAsync(string productCode, CancellationToken cancellationToken = default);
}


using Rge.BuildingBlocks.Api.Endpoints;



namespace Rge.Accounting.Host.Composition;



/// <summary>

/// Central registry of all endpoint modules in the Modular Monolith.

/// </summary>

public sealed class ModuleRegistry

{

    private readonly List<IEndpointModule> _modules = [];



    public IReadOnlyList<IEndpointModule> Modules => _modules;



    public ModuleRegistry Register(IEndpointModule module)

    {

        ArgumentNullException.ThrowIfNull(module);

        _modules.Add(module);

        return this;

    }

}

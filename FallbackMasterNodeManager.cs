using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Server;

namespace Scai.OpcUa.Substitute;

/// <summary>
/// A MasterNodeManager that routes any NodeId not handled by the standard managers
/// to the <see cref="DynamicNodeManager"/>.
/// </summary>
public sealed class FallbackMasterNodeManager : MasterNodeManager
{
    private readonly DynamicNodeManager _fallback;

    public FallbackMasterNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration,
        DynamicNodeManager fallback,
        IEnumerable<IAsyncNodeManager> asyncManagers,
        IEnumerable<INodeManager> syncManagers)
        : base(server, configuration, null, asyncManagers, syncManagers)
    {
        _fallback = fallback;
    }

    public override object? GetManagerHandle(NodeId nodeId, out INodeManager? nodeManager)
    {
        object? handle = base.GetManagerHandle(nodeId, out nodeManager);
        if (handle != null) return handle;

        handle = _fallback.GetManagerHandle(nodeId);
        if (handle != null)
        {
            nodeManager = _fallback;
            return handle;
        }
        return null;
    }
}

using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Server;

namespace Scai.OpcUa.Substitute;

public sealed class SubstituteServer : StandardServer
{
    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration)
    {
        var syncManagers = new List<INodeManager>();
        foreach (INodeManagerFactory factory in NodeManagerFactories)
        {
            syncManagers.Add(factory.Create(server, configuration));
        }

        var asyncManagers = new List<IAsyncNodeManager>();
        foreach (IAsyncNodeManagerFactory factory in AsyncNodeManagerFactories)
        {
            asyncManagers.Add(factory.CreateAsync(server, configuration).AsTask().GetAwaiter().GetResult());
        }

        var dynamic = new DynamicNodeManager(server, configuration);
        syncManagers.Add(dynamic);

        return new FallbackMasterNodeManager(server, configuration, dynamic, asyncManagers, syncManagers);
    }
}

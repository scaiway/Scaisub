using System;
using System.Collections.Generic;
using System.IO;
using Opc.Ua;
using Opc.Ua.Server;

namespace Scai.OpcUa.Substitute;

/// <summary>
/// A node manager that accepts any NodeId a client requests — nodes are created on demand,
/// made browsable under a "DynamicNodes" folder, and persisted to disk so they are restored
/// as known nodes (with their last value) on the next server start.
/// </summary>
public sealed class DynamicNodeManager : CustomNodeManager2
{
    // Stores every node that has been requested by a client. Guarded by Lock.
    private readonly Dictionary<NodeId, BaseDataVariableState> _nodes = new();
    private readonly NodePersistenceStore _store;
    private FolderState _root = null!;

    public DynamicNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, "urn:dynamic-node-manager")
    {
        _store = new NodePersistenceStore(
            Path.Combine(AppContext.BaseDirectory, "DynamicNodes.json"),
            server.MessageContext);
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            _root = new FolderState(null)
            {
                NodeId = new NodeId("DynamicNodes", NamespaceIndex),
                BrowseName = new QualifiedName("DynamicNodes", NamespaceIndex),
                DisplayName = new LocalizedText("DynamicNodes"),
                TypeDefinitionId = ObjectTypeIds.FolderType,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                EventNotifier = EventNotifiers.None
            };
            AddPredefinedNode(SystemContext, _root);

            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, _root.NodeId));

            foreach ((NodeId nodeId, object? value) in _store.Load())
            {
                BaseDataVariableState node = CreateVariable(nodeId);
                if (value != null)
                {
                    node.Value = value;
                }
                RegisterNode(node);
            }
        }
    }

    // Skip the namespace check so we accept NodeIds from any namespace — except namespace 0,
    // which is the standard OPC UA address space and is always fully owned by the core node
    // manager, so an unrecognized id there is a real BadNodeIdUnknown, not a new client node.
    protected override NodeHandle? GetManagerHandle(
        ServerSystemContext context,
        NodeId nodeId,
        IDictionary<NodeId, NodeState> cache)
    {
        if (NodeId.IsNull(nodeId)) return null;

        if (nodeId.NamespaceIndex == 0)
        {
            lock (Lock)
            {
                if (!PredefinedNodes.ContainsKey(nodeId)) return null;
            }
        }

        var node = GetOrCreateNode(nodeId);
        var handle = new NodeHandle(nodeId, node);

        if (cache != null) cache[nodeId] = node;

        return handle;
    }

    // Accept any handle whose NodeId is already known to this manager
    // (covers both the DynamicNodes folder and the dynamically created variables).
    protected override NodeHandle? IsHandleInNamespace(object managerHandle)
    {
        if (managerHandle is not NodeHandle handle) return null;

        lock (Lock)
        {
            return PredefinedNodes.ContainsKey(handle.NodeId) ? handle : null;
        }
    }

    protected override NodeState? ValidateNode(
        ServerSystemContext context,
        NodeHandle handle,
        IDictionary<NodeId, NodeState> cache)
    {
        if (handle.Node != null) return handle.Node;

        var node = GetOrCreateNode(handle.NodeId);
        handle.Node = node;
        handle.Validated = true;
        return node;
    }

    // Returns the existing node (the DynamicNodes folder or a previously accepted
    // variable) if known, otherwise creates and registers a new variable for it.
    private NodeState GetOrCreateNode(NodeId nodeId)
    {
        lock (Lock)
        {
            if (PredefinedNodes.TryGetValue(nodeId, out NodeState? existing))
            {
                return existing;
            }

            BaseDataVariableState node = CreateVariable(nodeId);
            RegisterNode(node);
            return node;
        }
    }

    // Must be called while holding Lock.
    private void RegisterNode(BaseDataVariableState node)
    {
        AddPredefinedNode(SystemContext, node);
        _root.AddChild(node);
        _nodes[node.NodeId] = node;
        _store.Save(_nodes.Values);
    }

    private BaseDataVariableState CreateVariable(NodeId nodeId)
    {
        string name = nodeId.Identifier?.ToString() ?? nodeId.ToString();
        return new BaseDataVariableState(null)
        {
            NodeId = nodeId,
            BrowseName = new QualifiedName(name, nodeId.NamespaceIndex),
            DisplayName = new LocalizedText(name),
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            DataType = DataTypeIds.BaseDataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentReadOrWrite,
            UserAccessLevel = AccessLevels.CurrentReadOrWrite,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow,
            OnSimpleWriteValue = OnWriteNodeValue
        };
    }

    private ServiceResult OnWriteNodeValue(ISystemContext context, NodeState node, ref object value)
    {
        var variable = (BaseDataVariableState)node;
        variable.Value = value;
        variable.Timestamp = DateTime.UtcNow;

        lock (Lock)
        {
            _store.Save(_nodes.Values);
        }

        return ServiceResult.Good;
    }
}

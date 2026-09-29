# Scai.OpcUa.Substitute

An OPC UA server that accepts **any** NodeId a client asks for. Instead of returning
`BadNodeIdUnknown` for an unknown node, the server creates a read/write variable for it
on the fly, shows it under a `DynamicNodes` folder in the address space, and saves it to
disk so it is still there, with its last value, after a restart.

It is useful as a substitute server during development and testing, when clients expect
nodes that no real device provides yet.

Built on .NET 10 and the [OPC Foundation UA .NET Standard](https://github.com/OPCFoundation/UA-.NETStandard) stack.

## Features

- **Nodes on demand**: reading, writing, subscribing to or browsing any NodeId in a
  non-zero namespace (for example `ns=3;i=1` or `ns=2;s=Line1.Speed`) creates the node.
- **Browsable**: every created node is listed under `Objects/DynamicNodes`.
- **Any value type**: variables use `BaseDataType`, so clients can write any scalar value.
- **Persistence**: node ids and their last written values are stored in
  `DynamicNodes.json` and restored at startup.
- **Standard namespace untouched**: namespace 0 is still owned by the core node manager,
  so unknown ids there fail normally.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Getting started

```sh
dotnet run
```

The server prints its endpoints on startup and runs until you press `Ctrl+C`:

```
OPC UA Dynamic Server (Scai.OpcUa.Substitute)
Server started. Endpoints:
  opc.tcp://localhost:4841/Scai.OpcUa.Substitute
Press Ctrl-C to stop.
```

Connect with any OPC UA client (such as UaExpert) to `opc.tcp://localhost:4841/Scai.OpcUa.Substitute`.

## Configuration

Server settings are in `Scai.OpcUa.Substitute.Config.xml`, which is copied to the output directory
on build.

| Setting | Default |
| --- | --- |
| Endpoint | `opc.tcp://localhost:4841/Scai.OpcUa.Substitute` |
| Application URI | `urn:localhost:UA:Scai.OpcUa.Substitute` |
| Security policies | `None`, `Basic256Sha256` |
| User tokens | Anonymous, UserName |
| Certificate stores | `%LocalApplicationData%/Scai.OpcUa.Substitute/pki/` |

The application certificate is created automatically on first run.

> **Warning:** untrusted client certificates are auto-accepted (`Program.cs` and the
> config file). This is intended for development. Turn it off before using the server in
> production.

## Persistence

Created nodes are written to `DynamicNodes.json` in the application's base directory (next
to the executable, for example `bin/Debug/net10.0/`). The file is rewritten each time a node
is created or a value is written. Each entry stores the NodeId and the value as an OPC UA
JSON-encoded Variant, so every built-in type, including arrays, `LocalizedText` and
`ExtensionObject`, is restored with its data type:

```json
[
  {
    "NodeId": "ns=3;i=1",
    "Value": { "UaType": 12, "Value": "hello" }
  }
]
```

Writes are atomic (temp file + replace). If the file can't be read at startup, it is renamed
to `DynamicNodes.json.corrupt-<timestamp>` and the server starts empty; individual invalid
entries are skipped.

Delete the file to start with an empty address space.

## Project structure

| File | Purpose |
| --- | --- |
| `Program.cs` | Entry point: loads the configuration, checks certificates, starts the server. |
| `SubstituteServer.cs` | `StandardServer` subclass that installs the dynamic node manager. |
| `FallbackMasterNodeManager.cs` | Sends NodeIds that no standard manager handles to the dynamic node manager. |
| `DynamicNodeManager.cs` | Creates, registers and serves the on-demand variables. |
| `NodePersistenceStore.cs` | Loads and saves nodes and values as JSON. |
| `Scai.OpcUa.Substitute.Config.xml` | OPC UA application configuration. |

## License

Proprietary. Copyright (c) 2026 Nicklas Hammer Thygesen. All rights reserved. See [LICENSE](LICENSE).

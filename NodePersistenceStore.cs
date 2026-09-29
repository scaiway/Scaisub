using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua;

namespace Scai.OpcUa.Substitute;

/// <summary>
/// Persists the set of dynamically accepted node ids and their last known value to a JSON file,
/// so they can be restored as known, browsable nodes on the next server start.
/// Values are stored in the OPC UA JSON encoding, so every built-in type (including arrays,
/// LocalizedText, ExtensionObject, ...) round-trips with its data type intact.
/// </summary>
public sealed class NodePersistenceStore(string path, IServiceMessageContext messageContext)
{
    private readonly object _fileLock = new();

    // Never throws: an unreadable file is set aside and the server starts empty, and a bad
    // entry is skipped, so a damaged store can never prevent the server from starting.
    public IReadOnlyList<(NodeId NodeId, object? Value)> Load()
    {
        List<Entry>? entries;
        lock (_fileLock)
        {
            if (!File.Exists(path))
            {
                return [];
            }

            try
            {
                entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Keep the damaged file for inspection instead of overwriting it on the next save.
                string backup = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
                Console.Error.WriteLine($"Could not read {path} ({ex.Message}); moved it to {backup} and starting empty.");
                TryMove(path, backup);
                return [];
            }
        }

        if (entries == null)
        {
            return [];
        }

        var result = new List<(NodeId, object?)>(entries.Count);
        foreach (Entry entry in entries)
        {
            try
            {
                result.Add((NodeId.Parse(entry.NodeId), DecodeValue(entry)));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Skipping stored node '{entry.NodeId}': {ex.Message}");
            }
        }
        return result;
    }

    public void Save(IEnumerable<BaseDataVariableState> nodes)
    {
        var entries = new List<Entry>();
        foreach (BaseDataVariableState node in nodes)
        {
            entries.Add(new Entry
            {
                NodeId = node.NodeId.ToString(),
                Value = EncodeValue(new Variant(node.Value))
            });
        }

        string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });

        // Write to a temp file and swap it in, so a crash mid-write leaves the previous file intact.
        string tempPath = path + ".tmp";
        lock (_fileLock)
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not move {from} to {to}: {ex.Message}");
        }
    }

    private JsonElement? EncodeValue(Variant value)
    {
        if (value.Value == null)
        {
            return null;
        }

        using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
        encoder.WriteVariant("Value", value);
        using JsonDocument document = JsonDocument.Parse(encoder.CloseAndReturnText());
        return document.RootElement.GetProperty("Value").Clone();
    }

    private object? DecodeValue(Entry entry)
    {
        if (entry.Value is not { ValueKind: not JsonValueKind.Null } encoded)
        {
            return null;
        }

        using var decoder = new JsonDecoder($"{{\"Value\":{encoded.GetRawText()}}}", messageContext);
        return decoder.ReadVariant("Value").Value;
    }

    private sealed class Entry
    {
        public string NodeId { get; set; } = "";

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Value { get; set; }
    }
}

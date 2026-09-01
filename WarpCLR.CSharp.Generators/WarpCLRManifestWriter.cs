using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WarpCLR.CSharp.Generators;

internal static class WarpCLRManifestWriter
{
    private static readonly ImmutableArray<string> RequiredCapabilities =
    [
        "warp.core.scalar/0.1",
        "warp.core.parallel/0.1",
        "warp.core.buffers/0.1",
        "warp.memory.scoped/0.1",
    ];

    public static string Write(
        string producer,
        string producerVersion,
        ImmutableArray<WarpCLREntryModel> entries)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", "warpcil/0.1");
            writer.WriteString("producer", producer);
            writer.WriteString("producerVersion", producerVersion);
            writer.WriteStartArray("entries");
            foreach (WarpCLREntryModel entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("type", entry.TypeIdentity);
                writer.WriteString("method", entry.MethodIdentity);
                writer.WriteString("execution", GetExecutionName(entry.Execution));
                writer.WriteStartArray("parameterRoles");
                foreach (string role in entry.ParameterRoles)
                {
                    writer.WriteStringValue(role);
                }

                writer.WriteEndArray();
                writer.WriteStartArray("capabilities");
                foreach (string capability in RequiredCapabilities)
                {
                    writer.WriteStringValue(capability);
                }

                writer.WriteEndArray();
                writer.WriteString("graphHash", entry.GraphHashPlaceholder);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("hostImports");
            writer.WriteEndArray();
            writer.WriteStartArray("extensions");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string GetExecutionName(int execution) => execution switch
    {
        0 => "map",
        1 => "reduce-wrapping-sum",
        2 => "reduce-minimum",
        3 => "reduce-maximum",
        _ => "invalid",
    };
}

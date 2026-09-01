using System.Collections.Immutable;
using System.Text;

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
        var manifest = new StringBuilder();
        manifest.Append("{\"contract\":\"warpcil/0.1\",\"producer\":");
        AppendJsonString(manifest, producer);
        manifest.Append(",\"producerVersion\":");
        AppendJsonString(manifest, producerVersion);
        manifest.Append(",\"entries\":[");
        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            if (entryIndex != 0)
            {
                manifest.Append(',');
            }

            WarpCLREntryModel entry = entries[entryIndex];
            manifest.Append("{\"type\":");
            AppendJsonString(manifest, entry.TypeIdentity);
            manifest.Append(",\"method\":");
            AppendJsonString(manifest, entry.MethodIdentity);
            manifest.Append(",\"execution\":");
            AppendJsonString(manifest, GetExecutionName(entry.Execution));
            manifest.Append(",\"parameterRoles\":[");
            AppendJsonStringArray(manifest, entry.ParameterRoles);
            manifest.Append("],\"capabilities\":[");
            AppendJsonStringArray(manifest, RequiredCapabilities);
            manifest.Append("],\"graphHash\":");
            AppendJsonString(manifest, entry.GraphHashPlaceholder);
            manifest.Append('}');
        }

        manifest.Append("],\"hostImports\":[],\"extensions\":[]}");
        return manifest.ToString();
    }

    private static void AppendJsonStringArray(
        StringBuilder output,
        ImmutableArray<string> values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            if (index != 0)
            {
                output.Append(',');
            }

            AppendJsonString(output, values[index]);
        }
    }

    private static void AppendJsonString(StringBuilder output, string value)
    {
        output.Append('"');
        foreach (char character in value)
        {
            if (IsUnescapedJsonCharacter(character))
            {
                output.Append(character);
                continue;
            }

            switch (character)
            {
                case '"':
                    output.Append("\\\"");
                    break;
                case '\\':
                    output.Append("\\\\");
                    break;
                case '\b':
                    output.Append("\\b");
                    break;
                case '\f':
                    output.Append("\\f");
                    break;
                case '\n':
                    output.Append("\\n");
                    break;
                case '\r':
                    output.Append("\\r");
                    break;
                case '\t':
                    output.Append("\\t");
                    break;
                default:
                    AppendUnicodeEscape(output, character);
                    break;
            }
        }

        output.Append('"');
    }

    private static bool IsUnescapedJsonCharacter(char value) =>
        value is >= 'A' and <= 'Z' or
            >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            ' ' or '.' or '_' or '-' or '/';

    private static void AppendUnicodeEscape(StringBuilder output, char value)
    {
        const string hex = "0123456789ABCDEF";
        output.Append("\\u");
        output.Append(hex[(value >> 12) & 0x0F]);
        output.Append(hex[(value >> 8) & 0x0F]);
        output.Append(hex[(value >> 4) & 0x0F]);
        output.Append(hex[value & 0x0F]);
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

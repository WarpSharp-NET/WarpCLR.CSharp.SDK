using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using WarpCLR.CSharp.Contracts;
using WarpCLR.Verifier;

namespace WarpCLR.CSharp.Build;

internal static class WarpCLRAssemblyFinalizer
{
    private const string ManifestMetadataKey = "WarpCIL.Manifest";

    public static WarpCLRAssemblyFinalization FinalizeAssemblyFile(
        string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        string fullPath = Path.GetFullPath(assemblyPath);
        byte[] original = File.ReadAllBytes(fullPath);
        WarpCLRAssemblyFinalization result = FinalizeAssembly(original);
        if (result.Changed)
        {
            File.WriteAllBytes(fullPath, result.AssemblyBytes);
        }

        return result;
    }

    public static WarpCLRAssemblyFinalization FinalizeAssembly(
        ReadOnlyMemory<byte> assemblyBytes)
    {
        if (assemblyBytes.IsEmpty)
        {
            throw new ArgumentException(
                "The assembly cannot be empty.",
                nameof(assemblyBytes));
        }

        byte[] original = assemblyBytes.ToArray();
        using var stream = new MemoryStream(original, writable: false);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
        {
            throw Error(
                "WCSB1001",
                "The input does not contain ECMA-335 metadata.");
        }

        MetadataReader metadata = peReader.GetMetadataReader();
        if (!metadata.IsAssembly)
        {
            throw Error(
                "WCSB1001",
                "The input metadata does not define an assembly.");
        }

        string? manifest = ReadEmbeddedManifest(metadata);
        if (manifest is null)
        {
            return new WarpCLRAssemblyFinalization(
                original,
                module: null,
                hasManifest: false,
                changed: false);
        }

        RejectStrongName(peReader, metadata);
        var verifier = new WarpModuleVerifier();
        try
        {
            WarpVerifiedModule verified = verifier.Verify(original);
            return new WarpCLRAssemblyFinalization(
                original,
                verified,
                hasManifest: true,
                changed: false);
        }
        catch (WarpVerificationException exception)
            when (exception.Code == "WRPCIL2004")
        {
        }

        IReadOnlyList<WarpCLRManifestEntry> entries = ParseEntries(manifest);
        IReadOnlyDictionary<string, string> graphHashes = verifier.ComputeGraphHashes(original);
        byte[] candidate = original.ToArray();
        bool changed = false;
        foreach (WarpCLRManifestEntry entry in entries)
        {
            string actual = graphHashes[entry.Identity];
            if (string.Equals(entry.GraphHash, actual, StringComparison.Ordinal))
            {
                continue;
            }

            string placeholder = WarpCLRGraphHashPlaceholder.Compute(entry.Identity);
            if (!string.Equals(
                    entry.GraphHash,
                    placeholder,
                    StringComparison.Ordinal))
            {
                throw Error(
                    "WCSB1003",
                    $"Entry point '{entry.Identity}' has an unknown graph hash.");
            }

            ReplaceUnique(candidate, placeholder, actual, entry.Identity);
            changed = true;
        }

        WarpVerifiedModule module = verifier.Verify(candidate);
        return new WarpCLRAssemblyFinalization(
            candidate,
            module,
            hasManifest: true,
            changed);
    }

    private static string? ReadEmbeddedManifest(MetadataReader metadata)
    {
        string? manifest = null;
        AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
        foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);
            if (!IsAssemblyMetadataAttribute(metadata, attribute.Constructor))
            {
                continue;
            }

            BlobReader reader = metadata.GetBlobReader(attribute.Value);
            if (reader.ReadUInt16() != 1)
            {
                throw Error(
                    "WCSB1001",
                    "An assembly metadata attribute has an invalid value.");
            }

            string? key = reader.ReadSerializedString();
            string? value = reader.ReadSerializedString();
            ushort namedArgumentCount = reader.ReadUInt16();
            if (namedArgumentCount != 0 || reader.RemainingBytes != 0)
            {
                throw Error(
                    "WCSB1001",
                    "An assembly metadata attribute has unexpected data.");
            }

            if (!string.Equals(key, ManifestMetadataKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (manifest is not null)
            {
                throw Error(
                    "WCSB1001",
                    "The assembly contains more than one WarpCIL manifest.");
            }

            manifest = value
                ?? throw Error(
                    "WCSB1001",
                    "The WarpCIL manifest value cannot be null.");
        }

        return manifest;
    }

    private static bool IsAssemblyMetadataAttribute(
        MetadataReader metadata,
        EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference)
        {
            return false;
        }

        MemberReference member = metadata.GetMemberReference(
            (MemberReferenceHandle)constructor);
        if (member.Parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        TypeReference type = metadata.GetTypeReference(
            (TypeReferenceHandle)member.Parent);
        return string.Equals(
                   metadata.GetString(type.Namespace),
                   "System.Reflection",
                   StringComparison.Ordinal) &&
               string.Equals(
                   metadata.GetString(type.Name),
                   nameof(AssemblyMetadataAttribute),
                   StringComparison.Ordinal);
    }

    private static void RejectStrongName(
        PEReader peReader,
        MetadataReader metadata)
    {
        AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
        CorHeader? corHeader = peReader.PEHeaders.CorHeader;
        bool publicKey = !assembly.PublicKey.IsNil &&
            metadata.GetBlobBytes(assembly.PublicKey).Length != 0;
        bool signatureDirectory = corHeader?.StrongNameSignatureDirectory.Size > 0;
        bool signedFlag = (corHeader?.Flags & CorFlags.StrongNameSigned) != 0;
        if (publicKey || signatureDirectory || signedFlag)
        {
            throw Error(
                "WCSB1002",
                "Strong-name signing is not available in the C# 0.1.0 profile.");
        }
    }

    private static IReadOnlyList<WarpCLRManifestEntry> ParseEntries(string manifest)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(manifest);
            return document.RootElement
                .GetProperty("entries")
                .EnumerateArray()
                .Select(
                    entry => new WarpCLRManifestEntry(
                        entry.GetProperty("type").GetString()!,
                        entry.GetProperty("method").GetString()!,
                        entry.GetProperty("graphHash").GetString()!))
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw Error(
                "WCSB1001",
                $"The embedded manifest is invalid JSON. {exception.Message}");
        }
    }

    private static void ReplaceUnique(
        byte[] assembly,
        string current,
        string replacement,
        string identity)
    {
        byte[] currentBytes = Encoding.UTF8.GetBytes(current);
        byte[] replacementBytes = Encoding.UTF8.GetBytes(replacement);
        if (currentBytes.Length != replacementBytes.Length)
        {
            throw Error(
                "WCSB1003",
                $"Entry point '{identity}' has an invalid graph-hash length.");
        }

        int offset = assembly.AsSpan().IndexOf(currentBytes);
        if (offset < 0 ||
            assembly.AsSpan(offset + currentBytes.Length).IndexOf(currentBytes) >= 0)
        {
            throw Error(
                "WCSB1003",
                $"Entry point '{identity}' does not have one unique graph placeholder.");
        }

        replacementBytes.CopyTo(assembly.AsSpan(offset));
    }

    private static WarpCLRBuildException Error(
        string code,
        string message) => new(code, message);

    private sealed class WarpCLRManifestEntry
    {
        public WarpCLRManifestEntry(
            string type,
            string method,
            string graphHash)
        {
            Type = type;
            Method = method;
            GraphHash = graphHash;
        }

        public string Type { get; }

        public string Method { get; }

        public string Identity => $"{Type}.{Method}";

        public string GraphHash { get; }
    }
}

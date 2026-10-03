using System.Collections;

namespace WarpCLR.CSharp;

public sealed class WarpUInt32Buffer : IReadOnlyList<uint>
{
    private readonly uint[] values;

    public WarpUInt32Buffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        values = new uint[length];
    }

    public WarpUInt32Buffer(ReadOnlySpan<uint> values)
    {
        this.values = values.ToArray();
    }

    internal WarpUInt32Buffer(uint[] values, bool takeOwnership)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = takeOwnership ? values : values.ToArray();
    }

    public int Length => values.Length;

    public int Count => values.Length;

    public uint this[int index]
    {
        get => values[index];
        set => values[index] = value;
    }

    public Span<uint> Span => values;

    public ReadOnlySpan<uint> ReadOnlySpan => values;

    public static WarpUInt32Buffer From(params uint[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new WarpUInt32Buffer(values, takeOwnership: false);
    }

    public uint[] ToArray() => values.ToArray();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ006:Use value-type enumerators",
        Justification = "Preserves the existing public IEnumerator<uint> return ABI; EnumerateValues and ReadOnlySpan provide allocation-free enumeration.")]
    public IEnumerator<uint> GetEnumerator() => EnumerateValues();

    public WarpUInt32BufferEnumerator EnumerateValues() => new(values);

    IEnumerator<uint> IEnumerable<uint>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal uint[] GetStorage() => values;
}

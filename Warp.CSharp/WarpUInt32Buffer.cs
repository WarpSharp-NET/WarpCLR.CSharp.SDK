using System.Collections;

namespace Warp.CSharp;

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

    public IEnumerator<uint> GetEnumerator() =>
        ((IEnumerable<uint>)values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => values.GetEnumerator();

    internal uint[] GetStorage() => values;
}

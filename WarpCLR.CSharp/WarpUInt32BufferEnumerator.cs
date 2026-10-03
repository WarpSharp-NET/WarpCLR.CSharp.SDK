using System.Collections;
using System.Runtime.InteropServices;

namespace WarpCLR.CSharp;

[StructLayout(LayoutKind.Auto)]
public struct WarpUInt32BufferEnumerator : IEnumerator<uint>
{
    private readonly uint[]? values;
    private int index;

    internal WarpUInt32BufferEnumerator(uint[] values)
    {
        this.values = values;
        index = -1;
    }

    public readonly uint Current
    {
        get
        {
            if (values is null || (uint)index >= (uint)values.Length)
            {
                throw new InvalidOperationException("The enumerator is not positioned on an item.");
            }

            return values[index];
        }
    }

    readonly object IEnumerator.Current => Current;

    public readonly WarpUInt32BufferEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        if (values is null)
        {
            return false;
        }

        if (index < values.Length - 1)
        {
            index++;
            return true;
        }

        index = values.Length;
        return false;
    }

    void IEnumerator.Reset() => index = -1;

    public readonly void Dispose()
    {
    }
}

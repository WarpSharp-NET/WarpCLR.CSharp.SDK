using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WarpCLR.CSharp.Testing;

internal static class TestCollectionAssertions
{
    public static T SingleItem<T>(this IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using IEnumerator<T> enumerator = source.GetEnumerator();
        Assert.IsTrue(enumerator.MoveNext(), "The collection must contain one item.");
        T result = enumerator.Current;
        Assert.IsFalse(enumerator.MoveNext(), "The collection must not contain a second item.");
        return result;
    }
}

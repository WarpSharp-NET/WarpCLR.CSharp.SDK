using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.CSharp.Contracts;

internal static class WarpCLRGraphHashPlaceholder
{
    public static string Compute(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException(
                "The entry identity cannot be empty.",
                nameof(identity));
        }
        byte[] input = Encoding.UTF8.GetBytes(
            $"WarpCLR.CSharp.GraphPlaceholder/0.1\0{identity}");
        byte[] hash;
        using (SHA256 sha256 = SHA256.Create())
        {
            hash = sha256.ComputeHash(input);
        }

        const string hex = "0123456789ABCDEF";
        var result = new char[hash.Length * 2];
        for (int index = 0; index < hash.Length; index++)
        {
            result[index * 2] = hex[hash[index] >> 4];
            result[(index * 2) + 1] = hex[hash[index] & 0x0F];
        }

        return new string(result);
    }
}

namespace WarpCLR.CSharp.Build;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine(
                "WCSB0001: Specify one assembly path.");
            return 2;
        }

        try
        {
            WarpCLRAssemblyFinalization result = WarpCLRAssemblyFinalizer
                .FinalizeAssemblyFile(args[0]);
            if (!result.HasManifest)
            {
                Console.Out.WriteLine(
                    "No WarpCIL manifest was found. The assembly was not changed.");
            }
            else if (result.Changed)
            {
                Console.Out.WriteLine(
                    "WarpCLR finalized and verified the assembly.");
            }
            else
            {
                Console.Out.WriteLine(
                    "WarpCLR verified the finalized assembly.");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}

namespace Razorshave.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        return args[0] switch
        {
            "build" => HandleBuild(args.AsSpan(1)),
            "-h" or "--help" => HandleHelp(),
            _ => HandleUnknown(args[0]),
        };
    }

    private static int HandleBuild(ReadOnlySpan<string> rest)
    {
        if (rest.Length == 0)
        {
            Console.Error.WriteLine("razorshave: 'build' requires a project path");
            Console.Error.WriteLine("usage: razorshave build <project-dir>");
            return 1;
        }
        return BuildCommand.Run(rest[0]);
    }

    private static int HandleHelp()
    {
        PrintUsage();
        return 0;
    }

    private static int HandleUnknown(string arg)
    {
        Console.Error.WriteLine($"razorshave: unknown command '{arg}'");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Razorshave CLI — transpile Blazor components to static JavaScript.");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build <project-dir>     Transpile the project at <project-dir>");
        Console.WriteLine("                          and write the bundle to <project-dir>/dist/");
        Console.WriteLine("  --help, -h              Show this help");
    }
}

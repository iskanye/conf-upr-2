using System.Text.RegularExpressions;

namespace ConfigUpr2;

partial class Program
{
    public static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run -- [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  -n, --name <name>         : Name of the package to analyze (required)");
        Console.WriteLine("  -t, --test                : Enable test-repository mode (treat repo as local file)");
        Console.WriteLine("  -r, --repo <url|path>     : Repository URL or path to test repository (only for test-repo)");
        Console.WriteLine("  -v, --version <version>   : Package version (optional)");
        Console.WriteLine("  -d, --max-depth <number>  : Maximum dependency depth (non-negative integer, default 5)");
        Console.WriteLine("  -f, --filter <substring>  : Substring to filter packages (optional)");
        Console.WriteLine("  -o, --order               : Show install order (optional)");
        Console.WriteLine("  -h, --help                : Show this help and exit");
    }

    public static int Error(string message)
    {
        Console.Error.WriteLine("Error: " + message);
        Console.Error.WriteLine();
        PrintUsage();
        return 1;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.Compiled)]
    private static partial Regex PackageName();

    static bool IsValidPackageName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return PackageName().IsMatch(name);
    }

    static async Task<int> MainWrapper(string[] args)
    {
        CliOptions opts;
        try
        {
            opts = CliOptions.ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message);
        }

        if (!IsValidPackageName(opts.PackageName))
            return Error("Package name is required and must contain only letters, digits, '.', '-' or '_'.");

        bool repoIsUrl = Uri.TryCreate(opts.Repo, UriKind.Absolute, out var parsedUri) &&
                         (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps);

        if (opts.TestRepoMode)
        {
            if (repoIsUrl)
                return Error("--test mode requires a file path to a test repository, not an HTTP/HTTPS URL.");
            if (!File.Exists(opts.Repo) && !Directory.Exists(opts.Repo))
                return Error($"Test repository file or directory does not exist: {opts.Repo}");
        }

        if (opts.MaxDepth < 0)
            return Error("--max-depth must be a non-negative integer.");

        Console.WriteLine("package_name={0}", opts.PackageName);
        Console.WriteLine("test_repo_mode={0}", opts.TestRepoMode);
        if (opts.TestRepoMode)
            Console.WriteLine("repo={0}", opts.Repo);
        Console.WriteLine("version={0}", opts.Version);
        Console.WriteLine("max_depth={0}", opts.MaxDepth);
        Console.WriteLine("filter={0}", opts.Filter);

        try
        {
            using var client = new HttpClient();
            var (adjacency, depths) = await DependencyUtils.BuildDependencyGraphBFS(client, opts);

            Console.WriteLine();
            Console.WriteLine("Dependency graph (node : depth):");
            foreach (var kv in depths.OrderBy(k => k.Value).ThenBy(k => k.Key))
            {
                Console.WriteLine("{0} : {1}", kv.Key, kv.Value);
            }

            Console.WriteLine();
            Console.WriteLine("Edges (parent -> child):");

            foreach (var parent in adjacency.Keys.OrderBy(x => x))
            {
                foreach (var child in adjacency[parent])
                {
                    Console.WriteLine("{0} -> {1}", parent, child);
                }
            }

            if (opts.OrderMode)
            {
                var (order, cycles) = InstallOrder.ComputeInstallOrder(adjacency, opts.PackageName);
                Console.WriteLine();
                Console.WriteLine("Install / load order (dependencies first):");
                int idx = 1;
                foreach (var p in order)
                    Console.WriteLine("{0}. {1}", idx++, p);

                if (cycles.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Detected cycles:");
                    foreach (var c in cycles)
                        Console.WriteLine("Cycle: {0}", string.Join(" -> ", c));
                }

                Console.WriteLine();
            }

        }
        catch (Exception ex)
        {
            return Error("Error while building dependency graph: " + ex.Message);
        }

        return 0;
    }

    static async Task<int> Main(string[] args)
    {
        return await MainWrapper(args);
    }
}

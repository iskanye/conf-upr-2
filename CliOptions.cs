namespace ConfigUpr2;

public struct CliOptions
{
    public string PackageName;
    public string Repo;
    public bool TestRepoMode;
    public string Version;
    public int MaxDepth;
    public string Filter;
    public bool OrderMode;
    public bool Visualize;

    public static CliOptions ParseArgs(string[] args)
    {
        var opts = new CliOptions();        
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-n":
                case "--name":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --name");
                    opts.PackageName = args[++i];
                    break;
                case "-r":
                case "--repo":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --repo");
                    opts.Repo = args[++i];
                    break;
                case "-t":
                case "--test":
                    opts.TestRepoMode = true;
                    break;
                case "-v":
                case "--version":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --version");
                    opts.Version = args[++i];
                    break;
                case "-d":
                case "--max-depth":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --max-depth");
                    if (!int.TryParse(args[++i], out var d)) throw new ArgumentException("--max-depth must be an integer");
                    opts.MaxDepth = d;
                    break;
                case "-f":
                case "--filter":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --filter");
                    opts.Filter = args[++i];
                    break;
                case "-o":
                case "--order":
                    opts.OrderMode = true;
                    break;
                case "-m":
                case "--mermaid":
                    opts.Visualize = true;
                    break;
                case "-h":
                case "--help":
                    Program.PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {a}");
            }
        }
        return opts;
    }
}

using System.Text.RegularExpressions;

namespace ConfigUpr2
{
    partial class Program
	{
		struct CliOptions
		{
			public string PackageName;
			public string Repo;
			public bool TestRepoMode;
			public string Version;
			public int MaxDepth;
			public string Filter;
		}

		static void PrintUsage()
		{
			Console.WriteLine("Usage: dotnet run -- [options]");
			Console.WriteLine("Options:");
			Console.WriteLine("  -n, --name <name>         : Name of the package to analyze (required)");
			Console.WriteLine("  -r, --repo <url|path>     : Repository URL or path to test repository (required)");
			Console.WriteLine("  -t, --test                : Enable test-repository mode (treat repo as local file)");
			Console.WriteLine("  -v, --version <version>   : Package version (optional)");
			Console.WriteLine("  -d, --max-depth <number>  : Maximum dependency depth (non-negative integer, default 5)");
			Console.WriteLine("  -f, --filter <substring>  : Substring to filter packages (optional)");
			Console.WriteLine("  -h, --help                : Show this help and exit");
		}

		static int ExitWithError(string message)
		{
			Console.Error.WriteLine("Error: " + message);
			Console.Error.WriteLine();
			PrintUsage();
			return 1;
		}

		static CliOptions ParseArgs(string[] args)
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
					case "-h":
					case "--help":
						PrintUsage();
						Environment.Exit(0);
						break;
					default:
						throw new ArgumentException($"Unknown argument: {a}");
				}
			}
			return opts;
		}

		static bool IsValidPackageName(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return false;
			return PackageName().IsMatch(name);
		}

		static int MainWrapper(string[] args)
		{
			CliOptions opts;
			try
			{
				opts = ParseArgs(args);
			}
			catch (ArgumentException ex)
			{
				return ExitWithError(ex.Message);
			}

			if (!IsValidPackageName(opts.PackageName))
			{
				return ExitWithError("Package name is required and must contain only letters, digits, '.', '-' or '_'.");
			}

			if (string.IsNullOrWhiteSpace(opts.Repo))
			{
				return ExitWithError("Repository URL or path is required.");
			}

			// Repo validation: if looks like URL, validate scheme; else treat as path and require file exists
			bool repoIsUrl = Uri.TryCreate(opts.Repo, UriKind.Absolute, out var parsedUri) &&
							 (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps);

			if (opts.TestRepoMode)
			{
				if (repoIsUrl)
				{
					return ExitWithError("--test mode requires a file path to a test repository, not an HTTP/HTTPS URL.");
				}
				if (!File.Exists(opts.Repo))
				{
					return ExitWithError($"Test repository file does not exist: {opts.Repo}");
				}
			}
			else if (repoIsUrl && !File.Exists(opts.Repo))
            {
				return ExitWithError($"Repository is not a valid HTTP/HTTPS URL and the file does not exist: {opts.Repo}");
			}

			if (opts.MaxDepth < 0)
			{
				return ExitWithError("--max-depth must be a non-negative integer.");
			}

			// All validation passed — print parameters key=value
			Console.WriteLine("package_name={0}", opts.PackageName);
			Console.WriteLine("repo={0}", opts.Repo);
			Console.WriteLine("test_repo_mode={0}", opts.TestRepoMode);
			Console.WriteLine("version={0}", string.IsNullOrEmpty(opts.Version) ? "" : opts.Version);
			Console.WriteLine("max_depth={0}", opts.MaxDepth);
			Console.WriteLine("filter={0}", string.IsNullOrEmpty(opts.Filter) ? "" : opts.Filter);

			return 0;
		}

		static int Main(string[] args)
		{
			return MainWrapper(args);
		}

        [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.Compiled)]
        private static partial Regex PackageName();
    }
}

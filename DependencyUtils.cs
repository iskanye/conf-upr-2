using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.Formats.Asn1;
using System.Linq;

namespace ConfigUpr2;

public static partial class DependencyUtils
{
    public static async Task<string?> GetPackageJson(CliOptions opts)
    {
        // Test repo: local file
        if (opts.TestRepoMode)
        {
            var path = opts.Repo;
            if (Directory.Exists(path))
            {
                var pkgPath = Path.Combine(path, "package.json");
                if (File.Exists(pkgPath))
                    return File.ReadAllText(pkgPath);

                throw new FileNotFoundException("package.json file not found.", pkgPath);
            }
            if (File.Exists(path))
                return File.ReadAllText(path);

            throw new FileNotFoundException("Test repository file not found.", path);
        }

        using var client = new HttpClient();
        // Repo is URL: try GitHub raw or npm registry
        if (Uri.TryCreate(opts.Repo, UriKind.Absolute, out var uri))
        {
            if (uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            {
                // expect form: https://github.com/{owner}/{repo} or with .git suffix
                var parts = uri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length >= 2)
                {
                    var owner = parts[0];
                    var repo = parts[1];

                    if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                        repo = repo[..^4];

                    var tag = string.IsNullOrEmpty(opts.Version) ?
                        "main" : 
                        (opts.Version.StartsWith("v") ? opts.Version : "v" + opts.Version);
                    var rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{tag}/package.json";

                    return await GetStringAsync(client, rawUrl);
                }

                throw new Exception("Invalid GitHub URL format.");
            }

            // Try npm registry pattern: https://registry.npmjs.org/<pkg>/<version>
            if (uri.Host.Contains("registry.npmjs.org", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Contains("npmjs.org", StringComparison.OrdinalIgnoreCase))
            {
                var parts = uri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length != 0)
                {
                    var pkg = parts[0];
                    var ver = parts.Length > 1 ? parts[1] : (string.IsNullOrEmpty(opts.Version) ? "latest" : opts.Version);

                    var regUrl = $"https://registry.npmjs.org/{pkg}/{ver}";

                    return await GetStringAsync(client, regUrl);
                }

                throw new Exception("Invalid npm registry URL format.");
            }
        }

        throw new Exception("Could not fetch package.json from the specified repository.");
    }

    /// <summary>
    /// Build dependency graph using BFS (non-recursive). Returns adjacency list and depths for each discovered node.
    /// In test mode opts.Repo should point to a graph description file (JSON or plain text). Package names in test graph are expected
    /// to be uppercase letters (per instructions) but any names are accepted. Filter substring and MaxDepth are honored.
    /// </summary>
    public static async Task<(Dictionary<string, List<string>>, Dictionary<string,int>)> BuildDependencyGraphBFS(CliOptions opts)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var filter = string.IsNullOrEmpty(opts.Filter) ? null : opts.Filter;

        // For test mode, parse the repository graph file as mapping
        Dictionary<string, List<string>>? testGraph = null;
        if (opts.TestRepoMode)
        {
            testGraph = ParseTestGraphFile(opts.Repo);
        }

        var q = new Queue<(string name, int depth)>();
        q.Enqueue((opts.PackageName, 0));
        depths[opts.PackageName] = 0;

        while (q.Count > 0)
        {
            var (name, depth) = q.Dequeue();

            if (visited.Contains(name)) 
                continue;
            visited.Add(name);

            // Apply filter: skip nodes containing the filter substring
            if (!string.IsNullOrEmpty(filter) && name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            // Ensure adjacency entry exists
            if (!adjacency.ContainsKey(name)) 
                adjacency[name] = [];

            // If reached max depth, do not expand further
            if (depth >= opts.MaxDepth)
                continue;

            void DepsToQueue(IEnumerable<string> deps)
            {
                foreach (var dep in deps)
                {
                    if (!string.IsNullOrEmpty(filter) && dep.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    adjacency[name].Add(dep);

                    if (!depths.ContainsKey(dep)) 
                        depths[dep] = depth + 1;
                    if (!visited.Contains(dep)) 
                        q.Enqueue((dep, depth + 1));
                }
            }

            if (opts.TestRepoMode)
            {
                // In test graph, dependencies are simple list of package names
                if (testGraph != null && testGraph.TryGetValue(name, out var list))
                    DepsToQueue(list);
            }
            else
            {
                // Fetch dependencies for the package from npm registry (best-effort)
                try
                {
                    var depsDict = await FetchDependenciesForPackageAsync(name, opts.Version);
                    if (depsDict != null)
                        DepsToQueue(depsDict.Keys);
                }
                catch { /* if fetching fails, treat as no dependencies */ }
            }
        }

        return (adjacency, depths);
    }

    static Dictionary<string, List<string>> ParseTestGraphFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Test graph file not found.", path);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        
        if (ext == ".json")
        {
            var txt = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(txt);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("Test graph JSON must be an object mapping package -> array of deps.");
            
            var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in root.EnumerateObject())
            {
                var list = new List<string>();
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in prop.Value.EnumerateArray())
                    {
                        if (it.ValueKind == JsonValueKind.String) 
                            list.Add(it.GetString()!);
                    }
                }
                dict[prop.Name] = list;
            }
            return dict;
        }

        // Plain text format: lines like 'A: B C D' or 'A: B, C'
        var res = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;
            
            var parts = line.Split(':', 2);
            var key = parts[0].Trim();
            var list = new List<string>();

            if (parts.Length > 1)
            {
                var rhs = parts[1];
                var deps = rhs.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

                foreach (var d in deps)
                    list.Add(d.Trim());
            }
            
            res[key] = list;
        }
        return res;
    }

    static bool IsExactSemver(string s)
    {
        if (string.IsNullOrEmpty(s)) 
            return false;
        return Semver().IsMatch(s);
    }

    static async Task<Dictionary<string,string>?> FetchDependenciesForPackageAsync(string pkg, string? versionRange)
    {
        using var client = new HttpClient();
        // If versionRange looks like exact semver, try registry/<pkg>/<ver>
        string url;
        if (!string.IsNullOrEmpty(versionRange) && IsExactSemver(versionRange))
            url = $"https://registry.npmjs.org/{pkg}/{versionRange}";
        else
            url = $"https://registry.npmjs.org/{pkg}";

        var txt = await GetStringAsync(client, url);
        if (string.IsNullOrEmpty(txt)) 
            return null;

        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;

        // If this is version object containing dependencies
        if (root.TryGetProperty("dependencies", out var deps))
            return ReadDependencies(deps);

        // If this is package metadata with 'versions' and possibly 'dist-tags'
        if (root.TryGetProperty("versions", out var versions))
        {
            // Try requested versionRange first
            if (!string.IsNullOrEmpty(versionRange) && versions.TryGetProperty(versionRange, out var verObj) && verObj.ValueKind == JsonValueKind.Object)
            {
                if (verObj.TryGetProperty("dependencies", out var d2)) 
                    return ReadDependencies(d2);
            }

            throw new JsonException("No 'dependencies' section found for the specified version.");
        }

        throw new JsonException("No 'dependencies' section found for the package.");
    }

    public static Dictionary<string, string> ExtractDirectDependencies(string packageJson)
    {
        using var doc = JsonDocument.Parse(packageJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("dependencies", out var deps))
            return ReadDependencies(deps);

        throw new JsonException("No 'dependencies' section found in package.json.");
    }

    static Dictionary<string, string> ReadDependencies(JsonElement depsElem)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (depsElem.ValueKind != JsonValueKind.Object)
            throw new JsonException("'dependencies' section is not a JSON object.");

        foreach (var p in depsElem.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                dict[p.Name] = p.Value.GetString() ?? "";
            }
        }
        return dict;
    }

    static async Task<string?> GetStringAsync(HttpClient client, string url)
    {
        var txt = await client.GetStringAsync(url);
        if (!string.IsNullOrEmpty(txt))
            return txt;

        throw new Exception($"Failed to fetch content from URL: {url}");
    }

    [System.Text.RegularExpressions.GeneratedRegex("^\\d+\\.\\d+\\.\\d+$")]
    private static partial System.Text.RegularExpressions.Regex Semver();
}

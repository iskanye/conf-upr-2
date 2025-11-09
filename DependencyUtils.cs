using System.Text.Json;

namespace ConfigUpr2;

public static partial class DependencyUtils
{
    public static async Task<(Dictionary<string, List<string>>, Dictionary<string, int>)> BuildDependencyGraphBFS(
        HttpClient client, CliOptions opts)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var filter = string.IsNullOrEmpty(opts.Filter) ? null : opts.Filter;

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

            if (!string.IsNullOrEmpty(filter) && name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!adjacency.ContainsKey(name))
                adjacency[name] = [];

            if (opts.MaxDepth != 0 && depth >= opts.MaxDepth)
                continue;

            void DepsToQueue(IEnumerable<string> deps)
            {
                foreach (var dep in deps)
                {
                    if (!string.IsNullOrEmpty(filter) && dep.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    var depDepth = depth + 1;
                    if (depDepth <= opts.MaxDepth || opts.MaxDepth == 0)
                    {
                        adjacency[name].Add(dep);
                        if (!depths.ContainsKey(dep))
                            depths[dep] = depDepth;
                        if (!visited.Contains(dep))
                            q.Enqueue((dep, depDepth));
                    }
                }
            }

            if (opts.TestRepoMode)
            {
                if (testGraph != null && testGraph.TryGetValue(name, out var list))
                    DepsToQueue(list);
            }
            else
            {
                try
                {
                    var versionArg = depth == 0 ? opts.Version : null;
                    var depsDict = await FetchDependenciesForPackageAsync(client, name, versionArg);
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
        if (Directory.Exists(path))
        {
            var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var files = Directory.GetFiles(path, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var f in files)
            {
                try
                {
                    var txt = File.ReadAllText(f);
                    using var doc = JsonDocument.Parse(txt);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("name", out var nameElem) && root.TryGetProperty("dependencies", out var depsElem))
                        {
                            var pkgName = nameElem.GetString() ?? Path.GetFileNameWithoutExtension(f);
                            var list = new List<string>();
                            if (depsElem.ValueKind == JsonValueKind.Object)
                            {
                                var map = ReadDependencies(depsElem);
                                foreach (var k in map.Keys) list.Add(k);
                            }
                            else if (depsElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var it in depsElem.EnumerateArray()) if (it.ValueKind == JsonValueKind.String) list.Add(it.GetString()!);
                            }
                            dict[pkgName] = list;
                            continue;
                        }

                        foreach (var prop in root.EnumerateObject())
                        {
                            var list = new List<string>();
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var it in prop.Value.EnumerateArray()) if (it.ValueKind == JsonValueKind.String) list.Add(it.GetString()!);
                            }
                            else if (prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                var depsMap = ReadDependencies(prop.Value);
                                foreach (var k in depsMap.Keys) list.Add(k);
                            }
                            dict[prop.Name] = list;
                        }
                    }
                }
                catch { }
            }
            return dict;
        }

        if (!File.Exists(path))
            throw new FileNotFoundException("Test graph file not found.", path);

        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".json")
        {
            var txt = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(txt);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("Test graph JSON must be an object mapping package -> array of deps or a package.json.");

            if (root.TryGetProperty("name", out var nameElem) && root.TryGetProperty("dependencies", out var depsElem))
            {
                var pkgName = nameElem.GetString() ?? Path.GetFileNameWithoutExtension(path);
                var list = new List<string>();
                if (depsElem.ValueKind == JsonValueKind.Object)
                {
                    var map = ReadDependencies(depsElem);
                    foreach (var k in map.Keys) 
                        list.Add(k);
                }
                else if (depsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in depsElem.EnumerateArray()) 
                        if (it.ValueKind == JsonValueKind.String) 
                            list.Add(it.GetString()!);
                }
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) { [pkgName] = list };
            }

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
                else if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var depsMap = ReadDependencies(prop.Value);
                    foreach (var k in depsMap.Keys)
                        list.Add(k);
                }

                dict[prop.Name] = list;
            }
            return dict;
        }

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

    static async Task<Dictionary<string, string>?> FetchDependenciesForPackageAsync(HttpClient client, string pkg, string? versionRange)
    {
        string url;
        if (!string.IsNullOrEmpty(versionRange) && IsExactSemver(versionRange))
            url = $"https://registry.npmjs.org/{pkg}/{versionRange}";
        else
            url = $"https://registry.npmjs.org/{pkg}/latest";

        var txt = await GetStringAsync(client, url);
        if (string.IsNullOrEmpty(txt)) 
            return null;

        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;

        if (root.TryGetProperty("dependencies", out var deps))
            return ReadDependencies(deps);

        throw new JsonException("No 'dependencies' section found for the package.");
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

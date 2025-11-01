using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;

namespace ConfigUpr2
{
    public static class DependencyUtils
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
                        
                    return null;
                }
                if (File.Exists(path))
                    return File.ReadAllText(path);
                    
                return null;
            }

            using var client = new HttpClient();
            // Repo is URL: try GitHub raw or npm registry
            if (Uri.TryCreate(opts.Repo, UriKind.Absolute, out var uri))
            {
                if (uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    // expect form: https://github.com/{owner}/{repo} or with .git suffix
                    var segments = uri.AbsolutePath.Trim('/').Split('/');
                    if (segments.Length >= 2)
                    {
                        string rawUrl;
                        var owner = segments[0];
                        var repo = segments[1];

                        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                            repo = repo[..^4];

                        // Try tag as-is and with 'v' prefix
                        var candidates = new[] { opts.Version, "v" + opts.Version };

                        foreach (var tag in candidates)
                        {
                            if (string.IsNullOrEmpty(tag))
                                continue;
                            rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{tag}/package.json";

                            try
                            {
                                var txt = await client.GetStringAsync(rawUrl);
                                if (!string.IsNullOrEmpty(txt))
                                    return txt;
                            }
                            catch { }
                        }

                        // As fallback try default branch
                        rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/main/package.json";

                        try
                        {
                            var txt = await client.GetStringAsync(rawUrl);

                            if (!string.IsNullOrEmpty(txt))
                                return txt;
                        }
                        catch { }
                    }
                }

                // Try npm registry pattern: https://registry.npmjs.org/<pkg>/<version>
                if (uri.Host.Contains("registry.npmjs.org", StringComparison.OrdinalIgnoreCase) 
                    || uri.Host.Contains("npmjs.org", StringComparison.OrdinalIgnoreCase))
                {
                    // If repo URL already points to registry with package name in path
                    // e.g., https://registry.npmjs.org/<pkg>/<version>
                    var parts = uri.AbsolutePath.Trim('/').Split('/');
                    if (parts.Length >= 2)
                    {
                        var pkg = parts[0];
                        var ver = parts.Length >= 2 ? parts[1] : opts.Version;

                        if (string.IsNullOrEmpty(ver))
                            ver = opts.Version;
                        if (string.IsNullOrEmpty(ver))
                            return null;

                        var regUrl = $"https://registry.npmjs.org/{pkg}/{ver}";

                        try
                        {
                            var txt = await client.GetStringAsync(regUrl);
                            if (!string.IsNullOrEmpty(txt))
                                // registry returns package metadata JSON; the package.json content is the metadata itself
                                return txt;
                        }
                        catch { }
                    }
                }

                // Generic attempt: if repo points directly to a raw package.json file
                try
                {
                    var txt = await client.GetStringAsync(opts.Repo);
                    if (!string.IsNullOrEmpty(txt))
                        return txt;
                }
                catch { }
            }

            return null;
        }

        public static Dictionary<string, string> ExtractDirectDependencies(string packageJson)
        {
            using var doc = JsonDocument.Parse(packageJson);
            var root = doc.RootElement;
            // If this JSON is from npm registry metadata, the 'version' object might be nested. Try to find 'dependencies' in common places.
            if (root.TryGetProperty("dependencies", out var deps))
                return ReadDependencies(deps);

            // registry metadata format: might be a full metadata with 'versions' object
            if (root.TryGetProperty("versions", out var vers))
            {
                // pick requested version key? If versions contains requested version, choose it; else take latest
                // Here we already fetched packageJson for the specific version when using registry URL, so 'versions' may be present; search for first object that has dependencies
                foreach (var prop in vers.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("dependencies", out deps))
                        return ReadDependencies(deps);
                }
            }

            // Some registry endpoints return the version object directly
            if (root.TryGetProperty("dependencies", out deps))
                return ReadDependencies(deps);

            return new Dictionary<string, string>();
        }

        static Dictionary<string, string> ReadDependencies(JsonElement depsElem)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (depsElem.ValueKind != JsonValueKind.Object)
                return dict;
            
            foreach (var p in depsElem.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    dict[p.Name] = p.Value.GetString() ?? "";
                }
            }
            return dict;
        }
    }
}

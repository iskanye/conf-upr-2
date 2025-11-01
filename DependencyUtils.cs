using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace ConfigUpr2;

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
                            return await GetStringAsync(client, rawUrl);
                        }
                        catch { }
                    }

                    // As fallback try default branch
                    rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/main/package.json";

                    return await GetStringAsync(client, rawUrl);
                }
                
                throw new Exception("Invalid GitHub URL format.");
            }

            // Try npm registry pattern: https://registry.npmjs.org/<pkg>/<version>
            if (uri.Host.Contains("registry.npmjs.org", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Contains("npmjs.org", StringComparison.OrdinalIgnoreCase))
            {
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

                    return await GetStringAsync(client, regUrl);
                }

                throw new Exception("Invalid npm registry URL format.");
            }
        }

        throw new Exception("Could not fetch package.json from the specified repository.");
    }

    public static Dictionary<string, string> ExtractDirectDependencies(string packageJson)
    {
        using var doc = JsonDocument.Parse(packageJson);
        var root = doc.RootElement;
        // If this JSON is from npm registry metadata, the 'version' object might be nested. 
        // Try to find 'dependencies' in common places.
        if (root.TryGetProperty("dependencies", out var deps))
            return ReadDependencies(deps);

        // Nested 'dependencies' case
        if (root.TryGetProperty("version", out var vers) && vers.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in vers.EnumerateObject())
            {
                if (vers.TryGetProperty("dependencies", out var nestedDeps))
                    return ReadDependencies(nestedDeps);
            }
        }

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
}

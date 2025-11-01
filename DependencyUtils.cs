using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.Formats.Asn1;

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
}

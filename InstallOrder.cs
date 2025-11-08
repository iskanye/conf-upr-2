using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConfigUpr2;

public static class InstallOrder
{
    /// <summary>
    /// Compute an install/load order for the given package. Returns list in the order
    /// dependencies should be loaded (dependencies first), and a list of detected cycles (each cycle is a list of nodes).
    /// Currently implemented as a DFS post-order traversal over the adjacency graph. Works best in test-repo mode
    /// where a local directory of package.json files or mapping JSON is available. For remote registries, this will
    /// build the adjacency via the existing BFS fetch and then compute the order.
    /// </summary>
    public static async Task<(List<string> Order, List<List<string>> Cycles)> ComputeInstallOrderAsync(CliOptions opts)
    {
        Dictionary<string, List<string>> adjacency;

        if (opts.TestRepoMode)
        {
            adjacency = DependencyUtils.ParseTestGraphFile(opts.Repo);
        }
        else
        {
            // Build adjacency by doing a BFS traversal up to a reasonable depth (opts.MaxDepth or default 5)
            var (adj, _) = await DependencyUtils.BuildDependencyGraphBFS(opts);
            adjacency = adj;
        }

        var order = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycles = new List<List<string>>();

        void Dfs(string node)
        {
            if (visited.Contains(node)) return;
            if (visiting.Contains(node))
            {
                // found a cycle; record simple cycle trace
                cycles.Add(new List<string> { node });
                return;
            }

            visiting.Add(node);
            if (adjacency.TryGetValue(node, out var children))
            {
                foreach (var c in children)
                {
                    if (!visited.Contains(c))
                        Dfs(c);
                }
            }
            visiting.Remove(node);
            visited.Add(node);
            order.Add(node);
        }

        Dfs(opts.PackageName);

        return (order, cycles);
    }
}

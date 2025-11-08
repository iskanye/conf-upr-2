using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConfigUpr2;

public static class InstallOrder
{
    public static async Task<(List<string> Order, List<List<string>> Cycles)> ComputeInstallOrderAsync(CliOptions opts)
    {
        Dictionary<string, List<string>> adjacency;

        if (opts.TestRepoMode)
        {
            adjacency = DependencyUtils.ParseTestGraphFile(opts.Repo);
        }
        else
        {
            (adjacency, _) = await DependencyUtils.BuildDependencyGraphBFS(opts);
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
                cycles.Add([node]);
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

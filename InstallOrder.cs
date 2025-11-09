namespace ConfigUpr2;

public static class InstallOrder
{
    public static (List<string>, List<List<string>>) ComputeInstallOrder(
        Dictionary<string, List<string>> adjacency,
        string packageName)
    {
        var order = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycles = new List<List<string>>();

        void Dfs(string node)
        {
            if (visited.Contains(node))
                return;

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

        Dfs(packageName);

        return (order, cycles);
    }
}

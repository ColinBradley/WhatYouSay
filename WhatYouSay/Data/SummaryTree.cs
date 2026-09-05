namespace WhatYouSay.Data;

/// <summary>
/// Adjacency list, stitched in memory. EF cannot eager-load an arbitrary depth, so a read
/// pulls every node for the summary in one query and the tree is assembled here. A summary
/// is a few hundred nodes at the outside, so there is no closure table and no recursive CTE.
/// </summary>
public static class SummaryTree
{
    /// <summary>
    /// Wires <see cref="SummaryNode.Children"/> from the flat node list. Idempotent, so a
    /// summary EF has already fixed up is not double-linked.
    /// </summary>
    public static void Assemble(Summary summary)
    {
        var byId = summary.Nodes.ToDictionary(node => node.Id);

        foreach (var node in summary.Nodes)
        {
            node.Children.Clear();
        }

        foreach (var node in summary.Nodes.OrderBy(node => node.Ordinal).ThenBy(node => node.Id))
        {
            if (node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
            {
                parent.Children.Add(node);
            }
        }
    }

    /// <summary>One sibling group in display order, roots when <paramref name="parentId"/> is null.</summary>
    public static List<SummaryNode> Siblings(Summary summary, int? parentId)
    {
        return [.. summary.Nodes
            .Where(node => node.ParentId == parentId)
            .OrderBy(node => node.Ordinal)
            .ThenBy(node => node.Id)];
    }

    /// <summary>
    /// Closes the gaps in a sibling group, so a move or a delete cannot leave two nodes
    /// sharing an ordinal and falling back to key order to break the tie.
    /// </summary>
    public static void Renumber(List<SummaryNode> siblings)
    {
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].Ordinal = i;
        }
    }

    /// <summary>A node and everything under it, so a delete can take its subtree with it.</summary>
    public static IEnumerable<SummaryNode> Subtree(Summary summary, SummaryNode root)
    {
        yield return root;

        foreach (var child in summary.Nodes.Where(node => node.ParentId == root.Id).ToList())
        {
            foreach (var descendant in Subtree(summary, child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Depth-first, parents before their children — the order the tree reads in.</summary>
    public static IEnumerable<SummaryNode> Flatten(IEnumerable<SummaryNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var descendant in Flatten(node.Children))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Levels, not edges: roots alone are depth 1 and an empty tree is 0.</summary>
    public static int Depth(IEnumerable<SummaryNode> nodes)
    {
        var deepest = 0;

        foreach (var node in nodes)
        {
            deepest = Math.Max(deepest, 1 + Depth(node.Children));
        }

        return deepest;
    }
}

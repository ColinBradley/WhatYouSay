namespace WhatYouSay.Data;

/// <summary>
/// The branch rule, applied to a stored tree rather than to a submitted draft: every branch
/// has to end in something somebody actually wrote.
/// </summary>
/// <remarks>
/// The agent's copy of this rule runs over <c>NodeDraft</c> before anything is written. This
/// one exists because a human editor can break grounding a node at a time — adding a node
/// always produces an uncited leaf — so the check cannot run per edit. It runs at publish
/// instead, and the editor shows the same answer live.
/// </remarks>
public static class SummaryGrounding
{
    /// <summary>
    /// Leaves that end a branch with nothing cited on them or above them, in reading order.
    /// Empty means the tree is publishable.
    /// </summary>
    /// <remarks>
    /// Expects a summary loaded with references to withdrawn responses already filtered out,
    /// which is what makes a node whose only support was withdrawn come back as ungrounded.
    /// </remarks>
    public static IReadOnlyList<SummaryNode> Ungrounded(Summary summary)
    {
        var found = new List<SummaryNode>();

        foreach (var root in summary.Roots)
        {
            Check(root, supported: false);
        }

        return found;

        void Check(SummaryNode node, bool supported)
        {
            // Support inherits down a branch, so a child of a cited node need not re-cite.
            var grounded = supported || node.References.Count > 0;

            if (node.Children.Count == 0)
            {
                if (!grounded)
                {
                    found.Add(node);
                }

                return;
            }

            foreach (var child in node.Children)
            {
                Check(child, grounded);
            }
        }
    }
}

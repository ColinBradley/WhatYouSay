namespace WhatYouSay.Data;

/// <summary>
/// One node of a summary tree.
/// </summary>
public class SummaryNode
{
    public int Id { get; set; }

    /// <summary>
    /// On every node rather than inferred up the parent chain, so one query filtered on it
    /// loads a whole tree of arbitrary depth.
    /// </summary>
    public Guid SummaryId { get; set; }

    public Summary Summary { get; set; } = null!;

    public int? ParentId { get; set; }

    public SummaryNode? Parent { get; set; }

    public int Ordinal { get; set; }

    /// <summary>
    /// One assertion, terse: a few words to a sentence. Elaborating means adding a child, not
    /// writing a paragraph. Untyped — nothing on a node says what sort of thing it is, so
    /// nothing but its own text and its place in the tree distinguishes a heading from a
    /// finding.
    /// </summary>
    public required string Text { get; set; }

    /// <summary>Empty until <see cref="SummaryTree.Assemble"/> has run over the summary.</summary>
    public List<SummaryNode> Children { get; set; } = [];

    /// <summary>
    /// Empty is legal anywhere but a leaf: support inherits down a branch, so a node under a
    /// cited one need not cite again.
    /// </summary>
    public List<SummaryNodeReference> References { get; set; } = [];

    public List<NodeReaction> Reactions { get; set; } = [];
}

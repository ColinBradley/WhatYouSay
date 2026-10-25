using System.ComponentModel.DataAnnotations.Schema;

namespace WhatYouSay.Data;

/// <summary>
/// One version of a topic's overview and note tree.
/// Under the reviewed policy each generation run creates a new version.
/// The live summary is the exception: the summariser revises it in place.
/// </summary>
public class Summary
{
    public Guid Id { get; set; }

    public Guid TopicId { get; set; }

    public Topic Topic { get; set; } = null!;

    /// <summary>
    /// Narrative overview in markdown, two or three paragraphs. Deliberately NOT a prose
    /// duplicate of the tree — that is what stops the two representations drifting.
    /// </summary>
    public required string Body { get; set; }

    /// <summary>True until published. The live summary is published from the start.</summary>
    public bool IsDraft { get; set; } = true;

    public bool IsPublic { get; set; }

    /// <summary>
    /// Whether the summariser token may write to this version.
    /// A published version is only writable while it is the live summary.
    /// </summary>
    public bool IsAgentEditable { get; set; }

    /// <summary>
    /// Whether this is the topic's live summary, which the summariser keeps revising in place.
    /// A topic has at most one, and it is always published.
    /// </summary>
    public bool IsLive { get; set; }

    /// <summary>
    /// Counts writes to the overview and the tree, starting at 1.
    /// It is a concurrency token, so a save fails if another write landed after the summary was loaded.
    /// Changes to visibility and summariser access leave it alone.
    /// </summary>
    public int Revision { get; set; } = 1;

    /// <summary>
    /// Live responses when the agent last wrote this version.
    /// A different live count suggests the version is outdated, for summaries without an
    /// <see cref="AppliedInputRevision"/>.
    /// </summary>
    public int ResponseCountAtWrite { get; set; }

    /// <summary>
    /// The topic input revision that the summariser's last write was generated from.
    /// A lower value than the topic's <see cref="Topic.InputRevision"/> means newer feedback is waiting.
    /// Null when the last summariser write did not say, or the summariser has never written here.
    /// Human edits leave it alone.
    /// </summary>
    public int? AppliedInputRevision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>"agent" or "human", shown in the version list.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Every node in the tree, flat. Roots are the ones with no parent.
    /// Service reads leave archived nodes out.
    /// </summary>
    public List<SummaryNode> Nodes { get; set; } = [];

    /// <summary>
    /// The tops of the tree, in display order. What hangs below them is wired by
    /// <see cref="SummaryTree.Assemble"/>, which every service read runs.
    /// </summary>
    /// <remarks>
    /// Not mapped: a collection of entities on an entity is a navigation by convention, and
    /// EF would give this one a second foreign key of its own.
    /// </remarks>
    [NotMapped]
    public IEnumerable<SummaryNode> Roots =>
        this.Nodes.Where(node => node.ParentId is null).OrderBy(node => node.Ordinal).ThenBy(node => node.Id);

    /// <summary>Admins always; everyone else only once blessed and made public.</summary>
    public bool IsVisibleToPublic => !this.IsDraft && this.IsPublic;
}

using System.ComponentModel.DataAnnotations.Schema;

namespace WhatYouSay.Data;

/// <summary>
/// A version, not a living document. Each generation run creates a new one; human edits
/// are never stomped by a re-run.
/// </summary>
public class Summary
{
    public Guid Id { get; set; }

    public Guid SurveyId { get; set; }

    public Survey Survey { get; set; } = null!;

    /// <summary>
    /// Narrative overview in markdown, two or three paragraphs. Deliberately NOT a prose
    /// duplicate of the tree — that is what stops the two representations drifting.
    /// </summary>
    public required string Body { get; set; }

    /// <summary>True until a human blesses it. Agents may only write to drafts.</summary>
    public bool IsDraft { get; set; } = true;

    public bool IsPublic { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>"agent" or "human", shown in the version list.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Every node in the tree, flat. Roots are the ones with no parent.</summary>
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

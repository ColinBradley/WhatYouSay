using Microsoft.EntityFrameworkCore;

namespace WhatYouSay.Data;

public class WhatYouSayContext(DbContextOptions<WhatYouSayContext> options) : DbContext(options)
{
    public DbSet<Survey> Surveys => this.Set<Survey>();

    public DbSet<Response> Responses => this.Set<Response>();

    public DbSet<Summary> Summaries => this.Set<Summary>();

    public DbSet<SummaryNode> SummaryNodes => this.Set<SummaryNode>();

    public DbSet<SummaryNodeReference> References => this.Set<SummaryNodeReference>();

    public DbSet<NodeReaction> NodeReactions => this.Set<NodeReaction>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Covers DateTimeOffset? too. Without this, every OrderBy on a timestamp throws.
        builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Survey>(survey =>
        {
            survey.HasIndex(s => s.Code).IsUnique();
            survey.HasIndex(s => s.SummariserTokenHash);

            // Enums as strings throughout: a sqlite3 session should show "Anonymous", not
            // "2", and reordering members must never reinterpret existing rows.
            survey.Property(s => s.ResponseIdentity).HasConversion<string>();
        });

        model.Entity<Response>(response =>
        {
            response.HasIndex(r => r.AuthTokenHash);

            response.HasOne(r => r.Survey)
                .WithMany(s => s.Responses)
                .HasForeignKey(r => r.SurveyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Summary>(summary =>
        {
            summary.HasOne(s => s.Survey)
                .WithMany(s => s.Summaries)
                .HasForeignKey(s => s.SurveyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SummaryNode>(node =>
        {
            // Indexed because loading a tree is one query filtered on nothing else.
            node.HasIndex(n => n.SummaryId);

            node.HasOne(n => n.Summary)
                .WithMany(s => s.Nodes)
                .HasForeignKey(n => n.SummaryId)
                .OnDelete(DeleteBehavior.Cascade);

            node.HasOne(n => n.Parent)
                .WithMany(n => n.Children)
                .HasForeignKey(n => n.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SummaryNodeReference>(reference =>
        {
            reference.HasOne(r => r.Node)
                .WithMany(n => n.References)
                .HasForeignKey(r => r.NodeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Responses are soft-deleted, never hard-deleted, so this restrict should be
            // unreachable. It is here so a stray hard delete fails loudly rather than
            // silently taking summary citations with it.
            reference.HasOne(r => r.Response)
                .WithMany(r => r.References)
                .HasForeignKey(r => r.ResponseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<NodeReaction>(reaction =>
        {
            reaction.Property(r => r.Kind).HasConversion<string>();

            // One toggle per kind per responder per node, so Agree and Important can
            // coexist while neither can be double-counted.
            reaction.HasIndex(r => new { r.NodeId, r.ResponderTokenHash, r.Kind }).IsUnique();

            reaction.HasOne(r => r.Node)
                .WithMany(n => n.Reactions)
                .HasForeignKey(r => r.NodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

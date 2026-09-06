using Microsoft.EntityFrameworkCore;

namespace WhatYouSay.Data;

public class WhatYouSayContext : DbContext
{
    public WhatYouSayContext(DbContextOptions<WhatYouSayContext> options)
        : base(options)
    {
    }

    public DbSet<Topic> Topics => this.Set<Topic>();

    public DbSet<Response> Responses => this.Set<Response>();

    public DbSet<Summary> Summaries => this.Set<Summary>();

    public DbSet<SummaryNode> SummaryNodes => this.Set<SummaryNode>();

    public DbSet<SummaryNodeReference> References => this.Set<SummaryNodeReference>();

    public DbSet<NodeReaction> NodeReactions => this.Set<NodeReaction>();

    public DbSet<NodeComment> NodeComments => this.Set<NodeComment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Covers DateTimeOffset? too. Without this, every OrderBy on a timestamp throws.
        builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Topic>(topic =>
        {
            topic.HasIndex(s => s.Code).IsUnique();
            topic.HasIndex(s => s.SummariserTokenHash);

            // Enums as strings throughout: a sqlite3 session should show "Anonymous", not
            // "2", and reordering members must never reinterpret existing rows.
            topic.Property(s => s.ResponseIdentity).HasConversion<string>();
        });

        model.Entity<Response>(response =>
        {
            response.HasIndex(r => r.AuthTokenHash);

            response.HasOne(r => r.Topic)
                .WithMany(s => s.Responses)
                .HasForeignKey(r => r.TopicId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Summary>(summary =>
        {
            summary.HasOne(s => s.Topic)
                .WithMany(s => s.Summaries)
                .HasForeignKey(s => s.TopicId)
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

            // One toggle per kind per reactor per node. Conflicting kinds are allowed;
            // what must not happen is the same one counted twice.
            reaction.HasIndex(r => new { r.NodeId, r.ReactorTokenHash, r.Kind }).IsUnique();

            reaction.HasOne(r => r.Node)
                .WithMany(n => n.Reactions)
                .HasForeignKey(r => r.NodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<NodeComment>(comment =>
        {
            comment.HasIndex(c => c.NodeId);

            comment.HasOne(c => c.Node)
                .WithMany(n => n.Comments)
                .HasForeignKey(c => c.NodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

using Microsoft.EntityFrameworkCore;

namespace WhatYouSay.Data;

public class WhatYouSayContext(DbContextOptions<WhatYouSayContext> options) : DbContext(options)
{
    public DbSet<Survey> Surveys => this.Set<Survey>();

    public DbSet<Response> Responses => this.Set<Response>();

    public DbSet<Summary> Summaries => this.Set<Summary>();

    public DbSet<SummaryTopic> SummaryTopics => this.Set<SummaryTopic>();

    public DbSet<SummaryTopicPoint> SummaryTopicPoints => this.Set<SummaryTopicPoint>();

    public DbSet<SummaryTopicPointResponseReference> References => this.Set<SummaryTopicPointResponseReference>();

    public DbSet<PointReaction> PointReactions => this.Set<PointReaction>();

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

        model.Entity<SummaryTopic>(topic =>
        {
            topic.HasOne(t => t.Summary)
                .WithMany(s => s.Topics)
                .HasForeignKey(t => t.SummaryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SummaryTopicPoint>(point =>
        {
            point.HasOne(p => p.Topic)
                .WithMany(t => t.Points)
                .HasForeignKey(p => p.TopicId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SummaryTopicPointResponseReference>(reference =>
        {
            reference.HasOne(r => r.Point)
                .WithMany(p => p.References)
                .HasForeignKey(r => r.PointId)
                .OnDelete(DeleteBehavior.Cascade);

            // Responses are soft-deleted, never hard-deleted, so this restrict should be
            // unreachable. It is here so a stray hard delete fails loudly rather than
            // silently taking summary citations with it.
            reference.HasOne(r => r.Response)
                .WithMany(r => r.References)
                .HasForeignKey(r => r.ResponseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PointReaction>(reaction =>
        {
            reaction.Property(r => r.Kind).HasConversion<string>();

            // One toggle per kind per responder per point, so Agree and Important can
            // coexist while neither can be double-counted.
            reaction.HasIndex(r => new { r.PointId, r.ResponderTokenHash, r.Kind }).IsUnique();

            reaction.HasOne(r => r.Point)
                .WithMany(p => p.Reactions)
                .HasForeignKey(r => r.PointId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

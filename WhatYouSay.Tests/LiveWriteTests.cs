using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>The live write and the snapshot it is generated from.</summary>
[TestClass]
public class LiveWriteTests : DatabaseTest
{
    private const string AnnaSaid = "CI is slow. A full run is 22 minutes and it fails often.";

    private const string TomSaid = "On-call was brutal, fourteen pages in a week.";

    [TestMethod]
    public async Task The_first_live_write_creates_the_live_summary_with_its_input_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var input = await this.InputRevisionAsync(topic);

        var live = await this.Summaries().SaveLiveAsync(
            topic, Draft(input, null, Cites("CI is slow", anna, "22 minutes")), this.Cancellation);

        Assert.IsTrue(live.IsLive);
        Assert.IsTrue(live.IsVisibleToPublic);
        Assert.AreEqual(1, live.Revision);
        Assert.AreEqual(input, live.AppliedInputRevision);
    }

    [TestMethod]
    public async Task A_live_write_revises_the_live_summary_in_place()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom) = await this.LiveTopicAsync();
        var first = await this.FirstWriteAsync(topic, anna);
        var kept = first.Nodes.Single();

        await this.Responses().SubmitAsync(topic, Secrets.NewToken(), "Flaky tests too.", "Sam", this.Cancellation);

        var input = await this.InputRevisionAsync(topic);

        var second = await this.Summaries().SaveLiveAsync(
            topic,
            Draft(input, first.Revision, Carry(kept.Id), Cites("On-call hurts", tom, "fourteen pages")),
            this.Cancellation);

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(2, second.Revision);
        Assert.AreEqual(input, second.AppliedInputRevision);
        Assert.Contains(n => n.Id == kept.Id, second.Nodes);
        Assert.AreEqual(1, await mDb.Summaries.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task A_stale_summary_revision_is_refused_and_leaves_the_tree_intact()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom) = await this.LiveTopicAsync();
        var live = await this.FirstWriteAsync(topic, anna);

        // A person edits the live summary after the summariser read revision 1.
        await using (var editor = this.NewContext())
        {
            var editorTopic = await editor.Topics.SingleAsync(this.Cancellation);

            await new SummaryEditService(editor).SetBodyAsync(editorTopic, live.Id, "Edited by hand", this.Cancellation);
        }

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Summaries().SaveLiveAsync(
                topic,
                Draft(live.AppliedInputRevision, 1, Cites("On-call hurts", tom, "fourteen pages")),
                this.Cancellation));

        await using var fresh = this.NewContext();

        var stored = (await new SummaryService(fresh).FindAsync(live.Id, this.Cancellation))!;

        Assert.AreEqual("stale_revision", rejection.Reason);
        Assert.AreEqual("Edited by hand", stored.Body);
        Assert.AreEqual("CI is slow", stored.Nodes.Single().Text);
        Assert.IsFalse(await fresh.SummaryNodes.AnyAsync(n => n.ArchivedAt != null, this.Cancellation));
    }

    /// <summary>
    /// A slow generation finishing after more feedback arrived is still published, for the
    /// input it read, and the snapshot shows the newer feedback as pending.
    /// </summary>
    [TestMethod]
    public async Task An_older_input_revision_is_accepted_and_newer_input_stays_pending()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom) = await this.LiveTopicAsync();
        var read = await this.ReadAsync(topic);

        await this.Responses().SubmitAsync(topic, Secrets.NewToken(), "Flaky tests too.", "Sam", this.Cancellation);

        var live = await this.Summaries().SaveLiveAsync(
            topic,
            Draft(read.Topic.InputRevision, null, Cites("CI is slow", anna, "22 minutes"), Cites("On-call hurts", tom, "fourteen pages")),
            this.Cancellation);

        var after = await this.ReadAsync(topic);

        Assert.AreEqual(read.Topic.InputRevision, live.AppliedInputRevision);
        Assert.IsGreaterThan(read.Topic.InputRevision, after.Topic.InputRevision);
        Assert.IsTrue(after.IsPending);
        Assert.AreEqual(live.Revision, after.LiveSummary!.Revision);
    }

    [TestMethod]
    public async Task Concurrent_first_writes_create_one_live_summary()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom) = await this.LiveTopicAsync();
        var input = await this.InputRevisionAsync(topic);

        // The other runner's first write lands between this one's checks and its save.
        var race = new BeforeFirstSave(
            () => this.Summaries().SaveLiveAsync(
                topic, Draft(input, null, Cites("CI is slow", anna, "22 minutes")), this.Cancellation));

        await using var racing = this.NewContext(race);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => new SummaryService(racing).SaveLiveAsync(
                topic, Draft(input, null, Cites("On-call hurts", tom, "fourteen pages")), this.Cancellation));

        await using var fresh = this.NewContext();

        var live = await fresh.Summaries.SingleAsync(this.Cancellation);
        var stored = (await new SummaryService(fresh).FindAsync(live.Id, this.Cancellation))!;

        Assert.IsTrue(race.HasRun);
        Assert.AreEqual("live_summary_exists", rejection.Reason);
        Assert.IsTrue(live.IsLive);
        Assert.AreEqual("CI is slow", stored.Nodes.Single().Text);
    }

    [TestMethod]
    public async Task A_write_expecting_no_live_summary_is_refused_once_one_exists()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom) = await this.LiveTopicAsync();
        var live = await this.FirstWriteAsync(topic, anna);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Summaries().SaveLiveAsync(
                topic,
                Draft(live.AppliedInputRevision, null, Cites("On-call hurts", tom, "fourteen pages")),
                this.Cancellation));

        Assert.AreEqual("live_summary_exists", rejection.Reason);
        Assert.AreEqual(1, await mDb.Summaries.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Evidence_for_a_withdrawn_or_hidden_response_is_refused_and_leaves_the_tree_intact()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom) = await this.LiveTopicAsync();
        var live = await this.FirstWriteAsync(topic, anna);
        var sam = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), "Flaky tests too.", "Sam", this.Cancellation);
        var input = await this.InputRevisionAsync(topic);

        await this.Responses().WithdrawAsync(topic, tom, this.Cancellation);
        await new TopicAdminService(mDb).DeleteResponseAsync(topic, sam.Id, this.Cancellation);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Summaries().SaveLiveAsync(
                topic,
                Draft(input, live.Revision, Cites("On-call hurts", tom, "fourteen pages"), Cites("Flaky tests", sam, "Flaky tests")),
                this.Cancellation));

        await using var fresh = this.NewContext();

        var stored = (await new SummaryService(fresh).FindAsync(live.Id, this.Cancellation))!;

        Assert.AreEqual("unknown_response", rejection.Reason);
        Assert.HasCount(2, rejection.Failures);
        Assert.AreEqual(1, stored.Revision);
        Assert.AreEqual("CI is slow", stored.Nodes.Single().Text);
    }

    [TestMethod]
    public async Task A_quote_must_come_from_the_revision_it_names()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var live = await this.FirstWriteAsync(topic, anna);

        await this.Responses().EditAsync(topic, anna, "CI is slow. Deploys are worse.", "Anna", this.Cancellation);

        var input = await this.InputRevisionAsync(topic);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Summaries().SaveLiveAsync(
                topic,
                Draft(
                    input,
                    live.Revision,
                    Cites("Deploys are worse", anna, "Deploys are worse", revision: 1),
                    Cites("CI fails", anna, "fails often", revision: 9)),
                this.Cancellation));

        Assert.AreEqual("quote_not_found", rejection.Reason);
        Assert.AreEqual("unknown_revision", rejection.Failures[1].Reason);
        Assert.AreEqual(1, (await mDb.Summaries.SingleAsync(this.Cancellation)).Revision);
    }

    /// <summary>An edit during generation does not invalidate quotes of the wording that was read.</summary>
    [TestMethod]
    public async Task An_earlier_revision_of_an_edited_response_can_be_cited()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var input = await this.InputRevisionAsync(topic);

        await this.Responses().EditAsync(topic, anna, "CI is slow. Deploys are worse.", "Anna", this.Cancellation);

        var live = await this.Summaries().SaveLiveAsync(
            topic, Draft(input, null, Cites("Runs fail often", anna, "fails often", revision: 1)), this.Cancellation);

        var reference = live.Nodes.Single().References.Single();

        Assert.AreEqual(1, reference.RevisionNumber);
        Assert.AreEqual("fails often", reference.Quote);
    }

    [TestMethod]
    public async Task A_live_write_names_its_input_revision_and_every_quote_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var input = await this.InputRevisionAsync(topic);

        var unnamedInput = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Summaries().SaveLiveAsync(
                topic, Draft(null, null, Cites("CI is slow", anna, "22 minutes")), this.Cancellation));

        var unnamedRevision = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Summaries().SaveLiveAsync(
                topic, Draft(input, null, Cites("CI is slow", anna, "22 minutes", revision: null)), this.Cancellation));

        Assert.AreEqual("input_revision_required", unnamedInput.Reason);
        Assert.AreEqual("revision_required", unnamedRevision.Reason);
        Assert.AreEqual("/nodes/0/references/0/revision", unnamedRevision.Failures.Single().Path);
    }

    [TestMethod]
    public async Task An_input_revision_outside_what_the_summary_and_topic_have_seen_is_refused()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var input = await this.InputRevisionAsync(topic);

        var future = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Summaries().SaveLiveAsync(
                topic, Draft(input + 1, null, Cites("CI is slow", anna, "22 minutes")), this.Cancellation));

        var live = await this.FirstWriteAsync(topic, anna);

        var past = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Summaries().SaveLiveAsync(
                topic, Draft(input - 1, live.Revision, Cites("CI is slow", anna, "22 minutes")), this.Cancellation));

        Assert.AreEqual("input_revision_out_of_range", future.Reason);
        Assert.AreEqual("input_revision_out_of_range", past.Reason);
    }

    [TestMethod]
    public async Task A_reviewed_topic_refuses_live_writes()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync(SummaryPolicy.Reviewed);
        var input = await this.InputRevisionAsync(topic);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Summaries().SaveLiveAsync(
                topic, Draft(input, null, Cites("CI is slow", anna, "22 minutes")), this.Cancellation));

        Assert.AreEqual("summaries_reviewed", rejection.Reason);
        Assert.IsFalse(await mDb.Summaries.AnyAsync(this.Cancellation));
    }

    /// <summary>A write through the older endpoint cannot say what it read, so the live summary shows as pending.</summary>
    [TestMethod]
    public async Task An_older_write_to_the_live_summary_records_no_input_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var live = await this.FirstWriteAsync(topic, anna);

        var revised = await this.Summaries().SaveDraftAsync(
            topic,
            new SummaryDraft()
            {
                Body = "Overview",
                Nodes = [new NodeDraft() { Id = live.Nodes.Single().Id }],
                BasedOnRevision = live.Revision,
            },
            "agent",
            live.Id,
            this.Cancellation);

        Assert.IsNull(revised.AppliedInputRevision);
        Assert.IsTrue((await this.ReadAsync(topic)).IsPending);
    }

    [TestMethod]
    public async Task A_read_carries_responses_reactions_guidance_and_live_feedback()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom) = await this.LiveTopicAsync();

        await new TopicAdminService(mDb).UpdateGuidanceAsync(topic, "Retro notes", "Group by theme", "Short", this.Cancellation);
        await new ReactionService(mDb).ToggleOnResponseAsync(topic, tom.Id, Secrets.NewToken(), ReactionKind.Important, this.Cancellation);

        var live = await this.FirstWriteAsync(topic, anna);
        var node = live.Nodes.Single();
        var comments = new CommentService(mDb);
        var commenter = Secrets.NewToken();

        await new ReactionService(mDb).ToggleAsync(topic, node.Id, Secrets.NewToken(), ReactionKind.Disagree, this.Cancellation);
        await comments.AddAsync(topic, node.Id, commenter, "Only on main.", "Kim", this.Cancellation);

        var hidden = await comments.AddAsync(topic, node.Id, commenter, "Never mind.", "Kim", this.Cancellation);
        await comments.SetHiddenAsync(topic, hidden, null, true, true, this.Cancellation);

        var read = await this.ReadAsync(topic);

        Assert.AreEqual("Retro notes", read.Topic.ExpectedFeedback);
        Assert.AreEqual("Group by theme", read.Topic.SummarisingInstructions);
        Assert.AreEqual("Short", read.Topic.SummaryLengthTarget);
        Assert.HasCount(2, read.Responses);
        Assert.AreEqual(1, read.ResponseReactions[tom.Id].CountOf(ReactionKind.Important));
        Assert.AreEqual(live.Id, read.LiveSummary!.Id);
        Assert.AreEqual(1, read.NodeReactions[node.Id].CountOf(ReactionKind.Disagree));
        Assert.AreEqual("Only on main.", read.Comments.Single().Body);
        Assert.IsTrue(read.IsPending, "Feedback on the live summary arrived after it was written.");
        Assert.IsTrue(read.IsWritable);
    }

    [TestMethod]
    public async Task Comments_on_archived_nodes_are_left_out_of_the_current_tree()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom) = await this.LiveTopicAsync();
        var live = await this.FirstWriteAsync(topic, anna);

        await new CommentService(mDb).AddAsync(topic, live.Nodes.Single().Id, Secrets.NewToken(), "Old point.", null, this.Cancellation);

        var input = await this.InputRevisionAsync(topic);

        await this.Summaries().SaveLiveAsync(
            topic, Draft(input, live.Revision, Cites("On-call hurts", tom, "fourteen pages")), this.Cancellation);

        await using var fresh = this.NewContext();

        var freshTopic = await fresh.Topics.SingleAsync(this.Cancellation);

        Assert.IsEmpty(await new CommentService(fresh).ListAsync(freshTopic, live.Id, null, true, this.Cancellation));
        Assert.IsEmpty((await this.ReadAsync(topic)).Comments);
    }

    [TestMethod]
    public async Task An_unchanged_read_returns_only_its_version()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, _) = await this.LiveTopicAsync();

        await using var context = this.NewContext();

        var inputs = new SummariserInputService(context);
        var first = await inputs.ReadAsync(topic, [], this.Cancellation);
        var second = await inputs.ReadAsync(topic, [first.Version], this.Cancellation);

        Assert.IsNotNull(first.Input);
        Assert.AreEqual(first.Version, second.Version);
        Assert.IsNull(second.Input);
    }

    [TestMethod]
    public async Task The_version_moves_with_input_writes_and_access()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var versions = new List<string>() { await this.VersionAsync(topic) };

        await this.Responses().EditAsync(topic, anna, AnnaSaid + " Still.", "Anna", this.Cancellation);
        versions.Add(await this.VersionAsync(topic));

        var live = await this.FirstWriteAsync(topic, anna);
        versions.Add(await this.VersionAsync(topic));

        await new TopicAdminService(mDb).SetAgentEditableAsync(topic, live.Id, false, this.Cancellation);
        versions.Add(await this.VersionAsync(topic));

        Assert.HasCount(4, versions.Distinct());
        Assert.IsFalse((await this.ReadAsync(topic)).IsWritable);
    }

    private ResponseService Responses()
    {
        return new ResponseService(mDb);
    }

    private SummaryService Summaries()
    {
        return new SummaryService(mDb);
    }

    private async Task<int> InputRevisionAsync(Topic topic)
    {
        return await mDb.Topics
            .Where(t => t.Id == topic.Id)
            .Select(t => t.InputRevision)
            .SingleAsync(this.Cancellation);
    }

    /// <summary>A full read through a context of its own, as a request makes it.</summary>
    private async Task<SummariserInput> ReadAsync(Topic topic)
    {
        await using var context = this.NewContext();

        var read = await new SummariserInputService(context).ReadAsync(topic, [], this.Cancellation);

        return read.Input!;
    }

    private async Task<string> VersionAsync(Topic topic)
    {
        await using var context = this.NewContext();

        return (await new SummariserInputService(context).ReadAsync(topic, [], this.Cancellation)).Version;
    }

    /// <summary>Creates the live summary with one node citing <paramref name="response"/>.</summary>
    private async Task<Summary> FirstWriteAsync(Topic topic, Response response)
    {
        var input = await this.InputRevisionAsync(topic);

        return await this.Summaries().SaveLiveAsync(
            topic, Draft(input, null, Cites("CI is slow", response, "22 minutes")), this.Cancellation);
    }

    /// <summary>An open topic with two editable responses, submitted as a responder would.</summary>
    private async Task<(Topic Topic, Response Anna, Response Tom)> LiveTopicAsync(
        SummaryPolicy policy = SummaryPolicy.Live
    )
    {
        var topic = NewTopic(ResponseIdentity.Required);
        topic.SummaryPolicy = policy;

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        var anna = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);
        var tom = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), TomSaid, "Tom", this.Cancellation);

        return (topic, anna, tom);
    }

    private static SummaryDraft Draft(int? inputRevision, int? basedOnRevision, params NodeDraft[] nodes)
    {
        return new SummaryDraft()
        {
            Body = "Overview",
            Nodes = nodes,
            InputRevision = inputRevision,
            BasedOnRevision = basedOnRevision,
        };
    }

    private static NodeDraft Carry(int id)
    {
        return new NodeDraft() { Id = id };
    }

    /// <summary>A node quoting <paramref name="response"/>, by default from its current revision.</summary>
    private static NodeDraft Cites(string text, Response response, string quote)
    {
        return Cites(text, response, quote, response.RevisionNumber);
    }

    private static NodeDraft Cites(string text, Response response, string quote, int? revision)
    {
        return new NodeDraft()
        {
            Text = text,
            References = [new ReferenceDraft() { ResponseId = response.Id, Revision = revision, Quote = quote }],
        };
    }
}

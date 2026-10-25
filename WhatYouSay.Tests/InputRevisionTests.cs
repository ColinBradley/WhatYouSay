using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>
/// The input revision moves with every change the summariser reads, including changes that
/// leave the response count where it was, and never with a summary write.
/// </summary>
[TestClass]
public class InputRevisionTests : DatabaseTest
{
    private const string AnnaSaid = "CI is slow. A full run is 22 minutes and it fails often.";

    [TestMethod]
    public async Task Submitting_a_response_moves_the_input_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var before = await this.InputRevisionAsync(topic);

        await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);

        Assert.IsGreaterThan(before, await this.InputRevisionAsync(topic));
    }

    [TestMethod]
    public async Task An_edit_moves_the_input_revision_though_the_count_stays_the_same()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var anna = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);
        var before = await this.InputRevisionAsync(topic);

        await this.Responses().EditAsync(topic, anna, "CI is slow.", "Anna", this.Cancellation);

        Assert.IsGreaterThan(before, await this.InputRevisionAsync(topic));
    }

    [TestMethod]
    public async Task Saving_a_response_unchanged_leaves_the_input_revision_alone()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var anna = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);
        var before = await this.InputRevisionAsync(topic);

        await this.Responses().EditAsync(topic, anna, AnnaSaid, "Anna", this.Cancellation);

        Assert.AreEqual(before, await this.InputRevisionAsync(topic));
    }

    [TestMethod]
    public async Task Replacing_one_response_with_another_moves_the_input_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var token = Secrets.NewToken();
        var anna = await this.Responses().SubmitAsync(topic, token, AnnaSaid, "Anna", this.Cancellation);
        var before = await this.InputRevisionAsync(topic);

        await this.Responses().WithdrawAsync(topic, anna, this.Cancellation);
        var between = await this.InputRevisionAsync(topic);

        await this.Responses().SubmitAsync(topic, token, "Deploys are slow too.", "Anna", this.Cancellation);

        Assert.AreEqual(1, await mDb.Responses.CountAsync(r => !r.IsDeleted, this.Cancellation));
        Assert.IsGreaterThan(before, between);
        Assert.IsGreaterThan(between, await this.InputRevisionAsync(topic));
    }

    [TestMethod]
    public async Task Hiding_a_response_moves_the_input_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var anna = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);
        var before = await this.InputRevisionAsync(topic);

        await new TopicAdminService(mDb).DeleteResponseAsync(topic, anna.Id, this.Cancellation);

        Assert.IsGreaterThan(before, await this.InputRevisionAsync(topic));
    }

    [TestMethod]
    public async Task Each_response_reaction_toggle_moves_the_input_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var anna = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);
        var reactions = new ReactionService(mDb);
        var reactor = Secrets.NewToken();
        var before = await this.InputRevisionAsync(topic);

        await reactions.ToggleOnResponseAsync(topic, anna.Id, reactor, ReactionKind.Agree, this.Cancellation);
        var reacted = await this.InputRevisionAsync(topic);

        await reactions.ToggleOnResponseAsync(topic, anna.Id, reactor, ReactionKind.Agree, this.Cancellation);

        Assert.IsGreaterThan(before, reacted);
        Assert.IsGreaterThan(reacted, await this.InputRevisionAsync(topic));
    }

    [TestMethod]
    public async Task Changing_guidance_moves_the_input_revision_and_saving_it_unchanged_does_not()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var admin = new TopicAdminService(mDb);
        var before = await this.InputRevisionAsync(topic);

        await admin.UpdateGuidanceAsync(topic, "Questions", "Group by team", null, this.Cancellation);
        var changed = await this.InputRevisionAsync(topic);

        await admin.UpdateGuidanceAsync(topic, "Questions", " Group by team ", "", this.Cancellation);

        Assert.IsGreaterThan(before, changed);
        Assert.AreEqual(changed, await this.InputRevisionAsync(topic));
    }

    [TestMethod]
    public async Task Feedback_on_the_live_summary_moves_the_input_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var anna = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);
        var live = await this.WriteLiveAsync(topic, anna);
        var node = live.Nodes.Single();
        var reader = Secrets.NewToken();
        var comments = new CommentService(mDb);

        var before = await this.InputRevisionAsync(topic);

        await new ReactionService(mDb).ToggleAsync(topic, node.Id, reader, ReactionKind.Disagree, this.Cancellation);
        var reacted = await this.InputRevisionAsync(topic);

        var comment = await comments.AddAsync(topic, node.Id, reader, "Not slow on main.", null, this.Cancellation);
        var commented = await this.InputRevisionAsync(topic);

        await comments.SetHiddenAsync(topic, comment, null, true, true, this.Cancellation);

        Assert.IsGreaterThan(before, reacted);
        Assert.IsGreaterThan(reacted, commented);
        Assert.IsGreaterThan(commented, await this.InputRevisionAsync(topic));
    }

    [TestMethod]
    public async Task Feedback_on_a_draft_leaves_the_input_revision_alone()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync(SummaryPolicy.Reviewed);
        var anna = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);
        var draft = await this.Summaries().SaveDraftAsync(
            topic, Draft(null, null, Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);
        var node = draft.Nodes.Single();
        var before = await this.InputRevisionAsync(topic);

        await new ReactionService(mDb).ToggleAsync(topic, node.Id, Secrets.NewToken(), ReactionKind.Agree, this.Cancellation);
        await new CommentService(mDb).AddAsync(topic, node.Id, Secrets.NewToken(), "Agreed.", null, this.Cancellation);

        Assert.AreEqual(before, await this.InputRevisionAsync(topic));
    }

    /// <summary>
    /// Every way a summary changes, including a summariser write that archives a node people
    /// reacted to, so a summariser can never trigger itself.
    /// </summary>
    [TestMethod]
    public async Task Summary_writes_leave_the_input_revision_alone()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var anna = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);
        var before = await this.InputRevisionAsync(topic);

        var live = await this.WriteLiveAsync(topic, anna);
        var first = live.Nodes.Single();

        await new ReactionService(mDb).ToggleAsync(topic, first.Id, Secrets.NewToken(), ReactionKind.Agree, this.Cancellation);
        await new CommentService(mDb).AddAsync(topic, first.Id, Secrets.NewToken(), "Yes.", null, this.Cancellation);

        var withFeedback = await this.InputRevisionAsync(topic);

        // Leaving the first node out archives it, with its reaction and comment.
        await this.Summaries().SaveLiveAsync(
            topic,
            Draft(withFeedback, live.Revision, Cites("Runs fail often", anna, "fails often")),
            this.Cancellation);

        var edits = new SummaryEditService(mDb);
        var admin = new TopicAdminService(mDb);

        await edits.SetBodyAsync(topic, live.Id, "Edited by hand", this.Cancellation);
        await edits.AddNodeAsync(topic, live.Id, null, "Added by hand", this.Cancellation);
        await admin.SetAgentEditableAsync(topic, live.Id, false, this.Cancellation);
        await admin.SetAgentEditableAsync(topic, live.Id, true, this.Cancellation);
        await this.Summaries().SaveDraftAsync(
            topic, Draft(null, null, Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        Assert.IsGreaterThan(before, withFeedback);
        Assert.AreEqual(withFeedback, await this.InputRevisionAsync(topic));
    }

    /// <summary>The database removes the node's reactions and comments with it, and that is a summary write too.</summary>
    [TestMethod]
    public async Task Deleting_a_node_with_feedback_leaves_the_input_revision_alone()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var anna = await this.Responses().SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);
        var live = await this.WriteLiveAsync(topic, anna);
        var node = live.Nodes.Single();

        await new ReactionService(mDb).ToggleAsync(topic, node.Id, Secrets.NewToken(), ReactionKind.Agree, this.Cancellation);
        await new CommentService(mDb).AddAsync(topic, node.Id, Secrets.NewToken(), "Yes.", null, this.Cancellation);

        var withFeedback = await this.InputRevisionAsync(topic);

        // A request of its own, which has not loaded the feedback it deletes.
        await using (var editor = this.NewContext())
        {
            var editorTopic = await editor.Topics.SingleAsync(this.Cancellation);

            await new SummaryEditService(editor).DeleteNodeAsync(editorTopic, live.Id, node.Id, this.Cancellation);
        }

        Assert.AreEqual(0, await mDb.NodeReactions.CountAsync(this.Cancellation));
        Assert.AreEqual(withFeedback, await this.InputRevisionAsync(topic));
    }

    [TestMethod]
    public async Task Another_topics_changes_leave_the_input_revision_alone()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.TopicAsync();
        var other = await this.TopicAsync();
        var before = await this.InputRevisionAsync(topic);

        await this.Responses().SubmitAsync(other, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);

        Assert.AreEqual(before, await this.InputRevisionAsync(topic));
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

    private async Task<Topic> TopicAsync(SummaryPolicy policy = SummaryPolicy.Live)
    {
        var topic = NewTopic(ResponseIdentity.Required);
        topic.SummaryPolicy = policy;

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return topic;
    }

    /// <summary>Creates the live summary with one node citing <paramref name="response"/>.</summary>
    private async Task<Summary> WriteLiveAsync(Topic topic, Response response)
    {
        var input = await this.InputRevisionAsync(topic);

        return await this.Summaries().SaveLiveAsync(
            topic, Draft(input, null, Cites("CI is slow", response, "22 minutes")), this.Cancellation);
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

    private static NodeDraft Cites(string text, Response response, string quote)
    {
        return new NodeDraft()
        {
            Text = text,
            References =
            [
                new ReferenceDraft() { ResponseId = response.Id, Revision = response.RevisionNumber, Quote = quote },
            ],
        };
    }
}

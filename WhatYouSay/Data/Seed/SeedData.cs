using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Services;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Data.Seed;

/// <summary>Development seed data: four topics of deliberately different shapes.</summary>
public static class SeedData
{
    /// <summary>Admin password for every seeded topic. Development only, obviously.</summary>
    public const string AdminPassword = "letmein";

    public static async Task EnsureSeededAsync(
        WhatYouSayContext db,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        if (await db.Topics.AnyAsync(cancellationToken))
        {
            activity?.SetTag("seed.skipped", true);
            return;
        }

        var retro = Retro();
        AddRetroSummary(retro);

        db.Topics.AddRange(retro, Takeaway(), CompanyWide(), Diary());
        await db.SaveChangesAsync(cancellationToken);

        var seeded = await db.Topics.AsNoTracking().ToListAsync(cancellationToken);

        activity?.SetTag("seed.topics.count", seeded.Count);
        activity?.SetTag("seed.admin.password", AdminPassword);
        activity?.SetTag("seed.topics", string.Join(", ", seeded.Select(topic => $"/topics/{topic.Code} — {topic.Title}")));
        activity?.SetTag("seed.summariser.tokens", string.Join(", ", sSummariserTokens));
    }

    /// <summary>
    /// Fixed rather than random so they can be pasted straight into an MCP config without
    /// digging through the database. Development only.
    /// </summary>
    private static readonly string[] sSummariserTokens =
        ["dev-retro", "dev-lunch", "dev-company", "dev-diary"];

    private static Topic Build(
        string code,
        string summariserToken,
        string title,
        string description,
        ResponseIdentity identity,
        bool acceptingResponses,
        (string? Author, string Body)[] responses
    )
    {
        var anonymous = identity == ResponseIdentity.Anonymous;
        var createdAt = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

        var topic = new Topic()
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Title = title,
            Description = description,
            AdminPasswordHash = Secrets.HashPassword(AdminPassword),
            SummariserTokenHash = Secrets.HashToken(summariserToken),
            IsPubliclyListed = true,
            IsAcceptingResponses = acceptingResponses,
            AreResponsesPublic = false,
            ResponseIdentity = identity,
            CreatedAt = createdAt,
        };

        for (var i = 0; i < responses.Length; i++)
        {
            var (author, body) = responses[i];

            topic.Responses.Add(new Response()
            {
                // v4, not v7: a time-ordered Guid would leak submission order and time in
                // anonymous topics. See ResponseService.SubmitAsync.
                Id = Guid.NewGuid(),
                Body = body,
                Author = anonymous ? null : author,
                AuthTokenHash = Secrets.HashToken(Secrets.NewToken()),
                // Staggered so ordering is realistic where it is recorded at all.
                CreatedAt = anonymous ? null : createdAt.AddHours(i * 1.7),
            });
        }

        return topic;
    }

    /// <summary>Named responses with overlapping themes and a causal chain. Closed, so it can be summarised.</summary>
    private static Topic Retro() =>
        Build(
            "spr47ab", "dev-retro",
            "Sprint 47 retro",
            "What went well, what didn't, and what should we change for sprint 48? Be as blunt as you like.",
            ResponseIdentity.Required,
            acceptingResponses: false,
            [
                ("Anna", "CI is the thing killing us. A full run is 22 minutes and it fails on flaky integration tests maybe one time in four, so a green build is more of an aspiration than a gate. I've started batching three or four commits before pushing just to avoid the wait, which is exactly the wrong behaviour and I know it."),
                ("Dev", "Pairing on the payments migration worked really well. Two days of it and we shipped something neither of us would have got right alone. I'd like more of it but it's hard to justify when the board is full."),
                ("Priya", "Acceptance criteria on the reporting tickets were basically 'make reports better'. I spent a day and a half building the wrong thing and only found out at review. Not blaming anyone, but the AC conversation needs to happen before the ticket is pulled, not during."),
                ("Tom", "On-call was brutal. Fourteen pages, eleven of them the same disk alert on the staging box that nobody owns. I got about four hours sleep on the Wednesday and was useless on the Thursday."),
                ("Sam", "The new design system components saved me a lot of time on the settings screens. Genuinely good work by whoever set up the tokens."),
                ("Marcus", "Reviews sit for a day or more. I put a PR up Monday morning and got the first comment Tuesday afternoon, by which point I'd context-switched twice and had to reload the whole thing in my head. The PRs are also getting bigger, which can't be helping."),
                ("Lena", "Standup is running to 25 minutes because we're doing status theatre for a manager who isn't even in the room. Can we go back to the three questions."),
                ("Jo", "Flaky tests, same three suites every time. We re-run them and move on, which means we've effectively switched off our own safety net. Nobody decided to do that, it just happened by degrees."),
                ("Ade", "Decent sprint from my side, got the search indexing done with no major blockers. The one thing I'd change is putting a time box on spike tickets, they always balloon."),
                ("Nils", "Too much work in progress. We had eleven tickets in flight across six people at one point. Everything was 90% done and nothing actually shipped until the Thursday."),
                ("Rae", "Staging was down for most of Tuesday and nobody knew who to ask. There's no clear ownership and it's turning into a running joke, except it costs us a day every time."),
                ("Kit", "More pairing please. Also can we stop pulling extra work into the sprint on day four, it makes the estimate meaningless and everyone knows it."),
            ]);

    /// <summary>The small case: five responses, one of them a single word.</summary>
    private static Topic Takeaway() =>
        Build(
            "lunch42", "dev-lunch",
            "Friday team lunch — what are we getting?",
            "Shout if you have a preference or an allergy. Ordering at 11.30.",
            ResponseIdentity.Optional,
            acceptingResponses: true,
            [
                ("Anna", "Thai please. The place on Mill Road does a good pad see ew and they're quick."),
                (null, "Anything but pizza, we've had pizza three Fridays running."),
                ("Priya", "I'm veggie so as long as there's more than one option that isn't a side salad I'm happy."),
                ("Tom", "Curry"),
                (null, "Genuinely don't mind, happy with whatever the majority wants. Mild preference for something we can eat at desks because I've got a 1pm."),
            ]);

    /// <summary>
    /// The scale case: anonymous, closed, many topics, and roughly a third low-effort
    /// answers. Anonymous means no timestamps, so ordering falls back to the random Guid.
    /// </summary>
    private static Topic CompanyWide() =>
        Build(
            "allco26", "dev-company",
            "What should we change about how we work in 2026?",
            "Anonymous and unfiltered. Anything about how we work, what gets in your way, or what you'd keep exactly as it is.",
            ResponseIdentity.Anonymous,
            acceptingResponses: false,
            [
                (null, "Meetings. I counted last week and I had 19 hours of scheduled calls, which leaves about a day and a half of actual working time. Most of them I contribute nothing to and could have read the notes."),
                (null, "The hybrid policy is three days in the office but nobody enforces it and it varies wildly by team, so half of us come in and find the floor empty. Either mean it or drop it."),
                (null, "Deployment takes 40 minutes and needs someone from platform to approve it. For a one-line copy change. We've built a process that assumes every change is dangerous."),
                (null, "Promotion criteria are a mystery. I've asked three people what the difference between a senior and a staff engineer is here and got three different answers, none of which matched the ladder document."),
                (null, "Genuinely happy. Good team, interesting problems, I get left alone to do the work. Not much I'd change."),
                (null, "Nobody knows what other teams are doing. We rebuilt a notifications service that another team had already shipped six months earlier. Two engineers, three months."),
                (null, "Tech debt never gets prioritised because it never has a customer name attached to it. Then we spend a quarter on an incident that was entirely predictable."),
                (null, "More focus time. Block out afternoons company-wide or something."),
                (null, "Onboarding was rough. Took me nine days to get a working local environment and most of that was waiting for access requests to be approved by people on holiday."),
                (null, "Pay. We all know the market rate and we all know where we sit relative to it. The total compensation philosophy deck did not help."),
                (null, "The all-hands is too polished. It's a broadcast, not a conversation, and the Q&A questions are clearly pre-screened. I'd rather have something rougher and honest."),
                (null, "Documentation is either missing or three years stale, and you can't tell which until you've followed it for an hour."),
                (null, "Too many priorities. Everything is P1 which means nothing is."),
                (null, "I like the flexible hours a lot. Being able to do the school run without negotiating it every time is worth more to me than a raise, honestly."),
                (null, "Interviews take five rounds and we still lose candidates to companies that made an offer in a week. We're not so desirable that we can afford that."),
                (null, "Whoever decided on the open plan office with no booths has never tried to take a customer call."),
                (null, "The on-call rota is fine on paper but three people carry most of it because they're the only ones who know the legacy stack."),
                (null, "More cross-team demos. The Friday showcase died during the reorg and nothing replaced it."),
                (null, "CI is slow."),
                (null, "Managers change too often. I've had four in two years and each one takes six months to understand what I do, then leaves."),
                (null, "Not enough recognition for the unglamorous work. The person who spent a month fixing our flaky tests made everyone faster and got nothing, while a flashy demo that shipped to nobody got a shout-out."),
                (null, "I would keep the engineering blog time. It's the only structured space we have to think."),
                (null, "Strategy changes every quarter and we're expected to be surprised each time."),
                (null, "Access control is a nightmare. Everything needs a ticket and the ticket needs an approver who doesn't know what the system is."),
                (null, "Honestly? Fewer dashboards, more talking to customers."),
                (null, "Remote people are second-class in hybrid meetings. If four people are in a room and two are dialled in, the two dialled in effectively aren't there."),
                (null, "Nothing to add really, other people have said it better."),
                (null, "The new laptop refresh policy is good. Small thing but it made a real difference."),
                (null, "We hire senior people and then don't let them decide anything."),
                (null, "Too much process for small changes and not enough for large ones. It's exactly backwards."),
                (null, "The internal tooling team is excellent and criminally under-resourced."),
                (null, "Please can we stop the mandatory fun. The escape room was not the problem the survey was pointing at."),
                (null, "Career conversations happen once a year in the review cycle and are really about ratings, not careers."),
                (null, "Handovers between teams are where everything goes to die. There's no owner during the gap and it can sit for weeks."),
                (null, "+1 to whatever everyone else says about build times."),
                (null, "I'd like clearer written decisions. Things get decided in a call and then three people remember it differently."),
                (null, "Good place to work overall. The main frustration is how long it takes to get anything approved."),
                (null, "Security reviews are a black box. You submit, you wait, you get a rejection with no explanation, you resubmit."),
                (null, "Not enough junior hiring. We're all seniors arguing about architecture and nobody's doing the groundwork."),
                (null, "Fine."),
                (null, "The office coffee situation is genuinely bad and I know that sounds trivial but it's the thing I notice every single day."),
                (null, "We measure output rather than outcome, so people ship things nobody uses and it counts as success."),
                (null, "Really good year for me personally. The mentoring scheme made a big difference and I'd like to see it expanded."),
                (null, "Standardise the tech stack. We have three frontend frameworks in production and no plan to converge."),
                (null, "Communication from leadership improved a lot after the reorg. Credit where it's due."),
                (null, "Reduce meetings, increase trust, ship more. Not complicated."),
                (null, "I don't feel able to say no to work, and I don't think that's my manager's fault, it's just how everything is scoped."),
                (null, "The quarterly planning process takes three weeks and produces a plan that survives about ten days."),
                (null, "Better laptops for the data team, ours are four years old and the models take hours."),
                (null, "no strong feelings"),
                (null, "It's the context switching that gets me. Three projects at once means I'm bad at all of them and feel guilty about it constantly."),
                (null, "Would like to see more internal mobility. Moving teams is treated as disloyalty rather than as a good thing."),
                (null, "Everything above about meetings, twice."),
                (null, "The incident review process is genuinely blameless and that's rare. Please don't lose it."),
                (null, "Give teams a real budget instead of making them beg for every tool."),
                (null, "More async written updates, fewer status meetings."),
            ]);

    /// <summary>Single-author case. Author is a date rather than a person.</summary>
    private static Topic Diary() =>
        Build(
            "diary08", "dev-diary",
            "August journal",
            "Daily notes. What happened, how it felt, what I noticed.",
            ResponseIdentity.Optional,
            acceptingResponses: true,
            [
                ("Mon 3 Aug", "Slept badly again, maybe five hours. Morning went to the migration work and it was fine but I was foggy by two. Walked out to get lunch and it helped more than I expected. Cooked properly in the evening for the first time in a week."),
                ("Tue 4 Aug", "Better sleep. Productive morning. The thing I keep noticing is that when I don't open email before ten, the whole day goes differently — I get one real piece of work done before anything else lands."),
                ("Wed 5 Aug", "Long day. Three hours of calls back to back and then trying to write something coherent afterwards, which never works. Should have blocked the afternoon. Ran in the evening anyway and felt better for it."),
                ("Thu 6 Aug", "Good day. Finished the thing I'd been circling for two weeks. It turned out to be about forty lines once I understood the problem, which is always the way."),
                ("Fri 7 Aug", "Tired but content. Team lunch was nice. Realised I haven't seen anyone outside work in about three weeks and that's probably the thing to fix rather than any of the work stuff."),
                ("Sat 8 Aug", "Did almost nothing and didn't feel guilty about it. Read most of the afternoon. Called Mum."),
                ("Sun 9 Aug", "Bit restless. Spent too long on the laptop for something that wasn't urgent. Slept early though."),
                ("Mon 10 Aug", "Back to it. Noticing the pattern that Mondays are always the worst sleep and I think it's Sunday evening dread rather than anything physical."),
                ("Tue 11 Aug", "Really good stretch of focus in the morning, two clear hours. That's the whole game, I think. Everything else is admin around the edges."),
                ("Wed 12 Aug", "Flat. Nothing wrong exactly, just no energy for any of it. Went for a walk at lunch which is usually the fix but it didn't really land today."),
            ]);

    /// <summary>A hand-written summary, so the summary page has structure to render without an agent.</summary>
    private static void AddRetroSummary(Topic topic)
    {
        var writtenAt = new DateTimeOffset(2026, 8, 21, 16, 30, 0, TimeSpan.Zero);

        var summary = new Summary()
        {
            Id = Guid.CreateVersion7(),
            Body = """
                Three things dominate this retro, and two of them turn out to be the same thing.

                The build is the loudest complaint: a 22 minute CI run that fails intermittently has
                pushed people into batching their commits, which makes pull requests larger, which
                plausibly explains why reviews now take more than a day to get a first comment. That
                is one loop rather than three separate problems, and the cheapest place to break it
                is the flaky tests.

                Separately the sprint lost its shape — too much in flight at once, work pulled in on
                day four, and acceptance criteria vague enough to cost a day and a half of rework.
                Unowned staging infrastructure cost roughly another day on top.

                Pairing and the new design system components were the clear positives, and both were
                raised without being asked about.
                """,
            IsDraft = false,
            IsPublic = true,
            CreatedBy = "human",
            CreatedAt = writtenAt,
            UpdatedAt = writtenAt,
        };


        summary.Nodes.AddRange(Tree(
            Node("The build feedback loop",
                Cites(topic, "A full CI run takes 22 minutes and fails often enough that a green build is not a reliable gate.",
                    [(0, "A full run is 22 minutes and it fails on flaky integration tests maybe one time in four")],
                    Cites(topic, "The same three suites are the ones that fail.",
                        [(7, "Flaky tests, same three suites every time")]),
                    Cites(topic, "Re-running a flake rather than fixing it has switched the safety net off by degrees.",
                        [(7, "We re-run them and move on, which means we've effectively switched off our own safety net")])),
                Cites(topic, "People have started batching commits to dodge the wait.",
                    [(0, "I've started batching three or four commits before pushing just to avoid the wait")],
                    // Cites nothing of its own: inherited support, which is what the branch rule allows.
                    Node("Which is the opposite of what fast feedback is meant to encourage, and the person doing it knows it."))),
            Node("Review latency",
                Cites(topic, "A first review comment arrives a day or more after the pull request goes up.",
                    [(5, "I put a PR up Monday morning and got the first comment Tuesday afternoon")],
                    Cites(topic, "By then the author has context-switched away and has to reload the whole change.",
                        [(5, "by which point I'd context-switched twice and had to reload the whole thing in my head")])),
                Cites(topic, "Pull requests are getting larger, which is consistent with the commit batching above.",
                    [
                        (5, "The PRs are also getting bigger, which can't be helping"),
                        (0, "I've started batching three or four commits before pushing just to avoid the wait"),
                    ])),
            Node("Sprint shape",
                Cites(topic, "Eleven tickets in flight across six people meant everything was nearly done and nothing shipped until Thursday.",
                    [(9, "Everything was 90% done and nothing actually shipped until the Thursday")]),
                Cites(topic, "Work pulled in on day four makes the original estimate meaningless.",
                    [(11, "can we stop pulling extra work into the sprint on day four")]),
                Cites(topic, "Vague acceptance criteria on the reporting tickets cost a day and a half of rework.",
                    [(2, "I spent a day and a half building the wrong thing and only found out at review")],
                    Cites(topic, "The acceptance criteria conversation wanted before the ticket is pulled, not during.",
                        [(2, "the AC conversation needs to happen before the ticket is pulled, not during")])),
                Cites(topic, "A time box on spike tickets wanted.",
                    [(8, "putting a time box on spike tickets, they always balloon")])),
            Node("Unowned infrastructure",
                Node("Staging",
                    Cites(topic, "Staging was down for most of a Tuesday with nobody to ask.",
                        [(10, "Staging was down for most of Tuesday and nobody knew who to ask")],
                        Cites(topic, "It has no owner and is becoming a running joke that costs a day each time.",
                            [(10, "There's no clear ownership and it's turning into a running joke")])),
                    Cites(topic, "On-call was dominated by a single repeating disk alert on that same box.",
                        [(3, "eleven of them the same disk alert on the staging box that nobody owns")],
                        Cites(topic, "The cost landed the next day rather than during the night.",
                            [(3, "I got about four hours sleep on the Wednesday and was useless on the Thursday")])))),
            Node("What worked",
                Cites(topic, "Pairing on the payments migration produced a better result than either person would have reached alone.",
                    [(1, "Two days of it and we shipped something neither of us would have got right alone")],
                    Cites(topic, "Hard to justify while the board is full, which ties back to work in progress.",
                        [(1, "it's hard to justify when the board is full")]),
                    Cites(topic, "More pairing wanted.",
                        [(11, "More pairing please")])),
                Cites(topic, "The new design system components saved real time on the settings screens.",
                    [(4, "saved me a lot of time on the settings screens")])),
            Node("Left open",
                Cites(topic, "Whether standup is still doing anything, or is status theatre for someone who is not in the room.",
                    [(6, "we're doing status theatre for a manager who isn't even in the room")]))));

        topic.Summaries.Add(summary);

        AddRetroReactions(topic, summary);
    }

    /// <summary>
    /// Reactions and comments from the people whose words the summary is built on, so the
    /// admin editor has objections to show without hand-building one.
    /// </summary>
    private static void AddRetroReactions(Topic topic, Summary summary)
    {
        var nodes = summary.Nodes;

        Comment(0, "A full CI run takes",
            "I said a green build is not reliable because of flaky tests, not that the 22 minutes "
            + "is the problem. Those are different complaints.");

        Comment(2, "A first review comment arrives",
            "This reads like I was complaining about the reviewers. I was complaining about the "
            + "queue.");

        React(1, "A full CI run takes", ReactionKind.Agree);
        React(2, "A full CI run takes", ReactionKind.Agree);
        React(0, "A first review comment arrives", ReactionKind.Disagree);
        React(3, "Staging was down for most of a Tuesday", ReactionKind.Important);
        React(1, "Pairing on the payments migration", ReactionKind.Celebrate);

        void React(int responderIndex, string nodeTextPrefix, ReactionKind kind)
        {
            if (Find(nodeTextPrefix) is not { } node)
            {
                return;
            }

            node.Reactions.Add(new NodeReaction()
            {
                ReactorTokenHash = topic.Responses[responderIndex].AuthTokenHash,
                Kind = kind,
                CreatedAt = topic.IsAnonymous ? null : topic.CreatedAt.AddDays(1),
            });
        }

        void Comment(int responderIndex, string nodeTextPrefix, string body)
        {
            if (Find(nodeTextPrefix) is not { } node)
            {
                return;
            }

            var responder = topic.Responses[responderIndex];

            node.Comments.Add(new NodeComment()
            {
                AuthorTokenHash = responder.AuthTokenHash,
                Author = responder.Author,
                Body = body,
                CreatedAt = topic.IsAnonymous ? null : topic.CreatedAt.AddDays(1),
            });
        }

        SummaryNode? Find(string nodeTextPrefix) =>
            nodes.FirstOrDefault(n => n.Text.StartsWith(nodeTextPrefix, StringComparison.Ordinal));
    }
    /// <summary>
    /// Flattens the tree the way the service does, because a summary stores every node in
    /// one list and only the parent links describe the shape.
    /// </summary>
    private static List<SummaryNode> Tree(params SummaryNode[] roots)
    {
        var nodes = new List<SummaryNode>();

        void Walk(SummaryNode node)
        {
            nodes.Add(node);

            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }

        for (var i = 0; i < roots.Length; i++)
        {
            roots[i].Ordinal = i;
            Walk(roots[i]);
        }

        return nodes;
    }

    private static SummaryNode Node(string text, params SummaryNode[] children)
    {
        var node = new SummaryNode() { Text = text };

        for (var i = 0; i < children.Length; i++)
        {
            children[i].Parent = node;
            children[i].Ordinal = i;
            node.Children.Add(children[i]);
        }

        return node;
    }

    private static SummaryNode Cites(
        Topic topic,
        string text,
        (int ResponseIndex, string Quote)[] citations,
        params SummaryNode[] children
    )
    {
        var node = Node(text, children);

        foreach (var (responseIndex, quote) in citations)
        {
            var response = topic.Responses[responseIndex];

            // A typo in a seed quote fails loudly here rather than rendering wrong.
            var location = QuoteLocator.Locate(response.Body, quote)
                ?? throw new InvalidOperationException(
                    $"Seed quote not found in response {responseIndex}: \"{quote}\"");

            node.References.Add(new SummaryNodeReference()
            {
                Response = response,
                Quote = quote,
                StartIndex = location.StartIndex,
                EndIndex = location.EndIndex,
            });
        }

        return node;
    }
}

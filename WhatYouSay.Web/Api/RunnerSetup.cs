using WhatYouSay.Data;

namespace WhatYouSay.Web.Api;

/// <summary>
/// The steps an admin copies to set up the runner for one topic. They work as instructions to
/// follow, or as a prompt for an assistant that helps with the setup. They leave out the
/// token, because the presenter sets it in their own terminal.
/// </summary>
public static class RunnerSetup
{
    // Paragraphs are single lines. A chat box reflows them, and hard wraps survive the
    // paste as ragged breaks.
    public static string For(Topic topic, string siteUrl, bool isAddressConfigured) =>
        $"""
        Set up the WhatYouSay summary runner for the topic "{topic.Title}", so that its live summary keeps itself up to date during the session.

        The runner is a small .NET script that runs on your computer for the whole session. Every 10 seconds it checks the topic. Whenever the feedback has changed, it asks your own Claude Code for the next version of the summary and publishes it. Keep the computer awake and online while it runs.
        {Policy(topic.SummaryPolicy)}
        1. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0, unless dotnet --version already shows 10 or later.

        2. Install Claude Code from https://code.claude.com/docs/en/setup, and sign in with the account that the session should use. This shows which account is signed in:

           claude auth status

        3. Download the runner into a folder of its own:

           PowerShell:      Invoke-WebRequest {siteUrl}{RunnerDownload.Path} -OutFile runner.cs
           macOS or Linux:  curl -o runner.cs {siteUrl}{RunnerDownload.Path}

        4. In the terminal that will run it, set the topic's summariser token. The admin page shows the token once, when it is regenerated. Type it in yourself rather than giving it to a chat.

           PowerShell:      $env:WHATYOUSAY_TOKEN = "<token>"
           macOS or Linux:  export WHATYOUSAY_TOKEN="<token>"

        5. Check that everything works. This makes one tiny generation:

           dotnet run runner.cs -- --site {siteUrl} --topic {topic.Code} --preflight

        6. When the session starts, run it until the time it should stop:

           dotnet run runner.cs -- --site {siteUrl} --topic {topic.Code} --until 17:30

           Add --model and a name, such as --model sonnet, to choose the model. Without it, Claude Code's default model is used. Ctrl+C stops the runner. Starting it again carries on with the same live summary.

        7. Open the presentation page and project it:

           {siteUrl}/topics/{topic.Code}/present

        {Address(siteUrl, isAddressConfigured)}
        """;

    private static string Policy(SummaryPolicy policy) =>
        policy == SummaryPolicy.Live
            ? string.Empty
            : "\nThis topic requires review at the moment, and the runner only updates a live summary. Before the session, choose \"Publish updates live\" on the topic's admin page.\n";

    private static string Address(string siteUrl, bool isAddressConfigured) =>
        isAddressConfigured
            ? $"The QR code on the presentation page is built on the site's public address, {siteUrl}."
            : $"The site has no public address yet, so the QR code on the presentation page uses {siteUrl}, which phones in the room may not reach. Whoever runs the site sets PublicBaseUrl to the address that phones use, then restarts it.";
}

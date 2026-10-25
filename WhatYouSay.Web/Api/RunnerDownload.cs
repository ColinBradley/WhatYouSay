namespace WhatYouSay.Web.Api;

/// <summary>
/// Serves the live summary runner at <see cref="Path"/>, so presenters download the version
/// that matches this site's contract. The script is embedded from <c>runner/runner.cs</c>.
/// </summary>
public static class RunnerDownload
{
    public const string Path = "/runner.cs";

    private static readonly byte[] sScript = Load();

    public static IEndpointRouteBuilder MapRunnerDownload(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, () => TypedResults.File(sScript, "text/plain; charset=utf-8", "runner.cs"));

        return endpoints;
    }

    private static byte[] Load()
    {
        using var stream = typeof(RunnerDownload).Assembly.GetManifestResourceStream("runner.cs")
            ?? throw new InvalidOperationException("The runner script is not embedded in the web assembly.");
        using var copy = new MemoryStream();

        stream.CopyTo(copy);

        return copy.ToArray();
    }
}

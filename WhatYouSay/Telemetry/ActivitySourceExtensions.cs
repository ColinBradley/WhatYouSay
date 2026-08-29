using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WhatYouSay.Telemetry;

public static class ActivitySourceExtensions
{
    /// <summary>Anchors a source path back to the repository root. Projects sit directly under it.</summary>
    private const string RepositoryAnchor = "/WhatYouSay";

    /// <summary>
    /// Starts an activity named after the calling member, tagged with the OpenTelemetry
    /// code attributes.
    /// </summary>
    public static Activity? Start(
        this ActivitySource source,
        string? activityName = null,
        ActivityKind kind = ActivityKind.Internal,
        ActivityContext? parentContext = null,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "",
        [CallerLineNumber] int callerLineNumber = 0)
    {
        var typeName = Path.GetFileNameWithoutExtension(callerFilePath);

        activityName ??= $"{typeName}.{callerMemberName}";

        var activity = parentContext is { } parent
            ? source.StartActivity(activityName, kind, parent)
            : source.StartActivity(activityName, kind);

        // Null when nothing is listening; skip the tag work rather than pay for it.
        if (activity is null)
        {
            return null;
        }

        var sourcePath = SourcePath(callerFilePath);

        activity.SetTag("code.function.name", QualifiedFunctionName(sourcePath, callerMemberName, typeName));
        activity.SetTag("code.file.path", sourcePath);
        activity.SetTag("code.line.number", callerLineNumber);

        return activity;
    }

    /// <summary>Repository-relative where possible, so traces do not carry build machine paths.</summary>
    internal static string SourcePath(string callerFilePath)
    {
        var normalised = callerFilePath.Replace('\\', '/');
        var anchor = normalised.LastIndexOf(RepositoryAnchor, StringComparison.OrdinalIgnoreCase);

        return anchor >= 0
            ? normalised[(anchor + 1)..]
            : Path.GetFileName(normalised);
    }

    internal static string QualifiedFunctionName(string sourcePath, string memberName, string typeName)
    {
        if (!sourcePath.Contains('/'))
        {
            return $"{typeName}.{memberName}";
        }

        var dotted = Path.ChangeExtension(sourcePath, extension: null).Replace('/', '.');

        return $"{dotted}.{memberName}";
    }
}

// The WhatYouSay live summary runner. It keeps one topic's live summary up to date during a
// session. It polls the site, and whenever the topic's input has changed it asks the
// presenter's own Claude Code for the next version of the summary, then publishes it.
//
// Needs the .NET 10 SDK and a signed-in Claude Code. The topic's summariser token comes from
// the WHATYOUSAY_TOKEN environment variable, or from "token" in a config file.
//
//   dotnet run runner.cs -- --site https://feedback.example.com --topic abc123 --preflight
//   dotnet run runner.cs -- --site https://feedback.example.com --topic abc123 --until 17:30
//
// Run it with --help for every option.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

Console.OutputEncoding = new UTF8Encoding(false);

return await Cli.RunAsync(args);

/// <summary>The process exit codes, for scripts and agents that start the runner.</summary>
internal static class ExitCodes
{
    /// <summary>The run ended normally. With --wait, the topic has input waiting for a summary.</summary>
    public const int Done = 0;

    /// <summary>Something needs fixing first: a setting, the token, the site or Claude Code.</summary>
    public const int Failed = 1;

    /// <summary>With --wait, the end time or Ctrl+C came before any input was waiting.</summary>
    public const int Stopped = 2;
}

internal enum RunMode
{
    Run,
    Preflight,
    Wait,
}

/// <summary>Reads the settings and runs the chosen mode.</summary>
internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        Settings? settings;

        try
        {
            settings = Settings.Parse(args);
        }
        catch (SettingsException problem)
        {
            Log.Error(problem.Message);
            Log.Plain("Run with --help to see every option.");

            return ExitCodes.Failed;
        }

        if (settings is null)
        {
            Log.Plain(Settings.Help);

            return ExitCodes.Done;
        }

        using var shutdown = new Shutdown(settings.EndsAt);
        using var site = new SiteClient(settings.Site, settings.Topic, settings.Tokens);

        if (settings.Mode == RunMode.Wait)
        {
            return await new Waiter(settings, site).RunAsync(shutdown.Token);
        }

        CheckpointStore store;

        try
        {
            store = CheckpointStore.Open(settings.StatePath, settings.Site, settings.Topic);
        }
        catch (IOException)
        {
            Log.Error(
                $"Another runner is already using {settings.StatePath}. Stop it first, or give this one its own --state file.");

            return ExitCodes.Failed;
        }

        using (store)
        {
            var model = new ClaudeCode(settings.ClaudePath, settings.Model, settings.Topic);

            try
            {
                var runner = new LiveRunner(settings, site, model, store, shutdown);

                return await runner.RunAsync();
            }
            finally
            {
                model.Dispose();
            }
        }
    }
}

/// <summary>
/// Turns Ctrl+C, a terminate signal and the end time into one cancellation.
/// A second Ctrl+C ends the process straight away.
/// </summary>
internal sealed class Shutdown : IDisposable
{
    private readonly CancellationTokenSource mSource = new();

    private readonly List<PosixSignalRegistration> mSignals = [];

    private readonly DateTimeOffset? mEndsAt;

    public Shutdown(DateTimeOffset? endsAt)
    {
        mEndsAt = endsAt;

        if (endsAt is { } end)
        {
            mSource.CancelAfter(Clamp(end - DateTimeOffset.Now));
        }

        mSignals.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, this.OnSignal));
        mSignals.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, this.OnSignal));
    }

    public CancellationToken Token =>
        mSource.Token;

    /// <summary>Why the run is stopping, for the last line it prints.</summary>
    public string Reason =>
        mEndsAt is { } end && DateTimeOffset.Now >= end ? "the end time was reached" : "it was asked to stop";

    public void Dispose()
    {
        foreach (var signal in mSignals)
        {
            signal.Dispose();
        }

        mSource.Dispose();
    }

    private static TimeSpan Clamp(TimeSpan delay)
    {
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private void OnSignal(PosixSignalContext context)
    {
        if (mSource.IsCancellationRequested)
        {
            return;
        }

        // Cancelling the default action keeps the process alive long enough to stop the model
        // client and save the checkpoint.
        context.Cancel = true;
        Log.Info("Stopping. Press Ctrl+C again to end straight away.");
        mSource.Cancel();
    }
}

/// <summary>Timestamped console output. Nothing written here ever includes the token.</summary>
internal static class Log
{
    private static readonly Lock sLock = new();

    public static void Info(string message)
    {
        Write(message, null);
    }

    public static void Warn(string message)
    {
        Write(message, ConsoleColor.Yellow);
    }

    public static void Error(string message)
    {
        Write(message, ConsoleColor.Red);
    }

    public static void Good(string message)
    {
        Write(message, ConsoleColor.Green);
    }

    /// <summary>An indented line with no timestamp, under the line before it.</summary>
    public static void Detail(string message)
    {
        lock (sLock)
        {
            Console.WriteLine($"         {message}");
        }
    }

    public static void Plain(string message)
    {
        lock (sLock)
        {
            Console.WriteLine(message);
        }
    }

    private static void Write(string message, ConsoleColor? colour)
    {
        lock (sLock)
        {
            Console.Write($"{DateTime.Now:HH:mm:ss} ");

            if (colour is { } c)
            {
                Console.ForegroundColor = c;
            }

            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}

internal sealed class SettingsException : Exception
{
    public SettingsException(string message)
        : base(message)
    {
    }
}

/// <summary>The startup settings, from the command line and an optional config file.</summary>
internal sealed partial record Settings
{
    public const string DefaultConfigFile = "whatyousay-runner.json";

    public const string Help =
        """
        WhatYouSay live summary runner

        Keeps a topic's live summary up to date: it checks the topic every few seconds, and
        when the feedback has changed it asks Claude Code for the next version of the summary.

        Usage:
          dotnet run runner.cs -- --site <url> --topic <code> [options]

        Required, on the command line or in the config file:
          --site <url>             The WhatYouSay site, such as https://feedback.example.com
          --topic <code>           The topic code, as in /topics/<code>

        The token:
          Set WHATYOUSAY_TOKEN to the topic's summariser token, or put it in the config file
          as "token". It is sent only in the Authorization header.

        Modes:
          --preflight              Check the site, the topic, Claude Code and one tiny
                                   generation, then exit.
          --wait                   Exit as soon as the topic has input waiting for a summary.
                                   Calls no model. Exit code 0 when input is waiting, 2 when
                                   the end time or Ctrl+C came first.

        Options:
          --until <time>           Stop at this local time, such as 17:30, or a full date and time.
          --duration <length>      Stop after this long, such as 90m or 2h.
          --model <name>           The model for Claude Code to use. Without it, Claude
                                   Code's default model is used.
          --interval <seconds>     How often to check the topic. Default 10.
          --timeout <seconds>      The longest one generation may take. Default 300.
          --claude <path>          The Claude Code executable, when it is not on PATH.
          --config <file>          A JSON config file. Default whatyousay-runner.json, if present.
          --state <file>           Where to keep the checkpoint. Default
                                   whatyousay-runner.<topic>.state.json.
          --max-input-chars <n>    The largest input to send to the model. Default 400000.
          --help                   Show this help.

        The config file takes the same settings by their long names, such as:
          { "site": "https://feedback.example.com", "topic": "abc123", "token": "...",
            "model": "sonnet", "interval": 10, "timeout": 300 }
        """;

    private static readonly HashSet<string> sFlags = ["--preflight", "--wait", "--help", "-h"];

    private static readonly HashSet<string> sOptions =
    [
        "--site", "--topic", "--model", "--interval", "--timeout", "--until", "--duration",
        "--claude", "--config", "--state", "--max-input-chars",
    ];

    public required Uri Site { get; init; }

    public required string Topic { get; init; }

    public required RunMode Mode { get; init; }

    public required TokenSource Tokens { get; init; }

    /// <summary>Null leaves the choice to the model client.</summary>
    public string? Model { get; init; }

    public required TimeSpan Interval { get; init; }

    public required TimeSpan GenerationTimeout { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    public string? ClaudePath { get; init; }

    public required string StatePath { get; init; }

    public required int MaxInputCharacters { get; init; }

    /// <summary>Returns null when the command line asks for help.</summary>
    public static Settings? Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (sFlags.Contains(arg))
            {
                flags.Add(arg);
            }
            else if (sOptions.Contains(arg))
            {
                if (i + 1 >= args.Length)
                {
                    throw new SettingsException($"{arg} needs a value.");
                }

                options[arg] = args[++i];
            }
            else
            {
                throw new SettingsException($"Unknown argument \"{arg}\".");
            }
        }

        if (flags.Contains("--help") || flags.Contains("-h"))
        {
            return null;
        }

        if (flags.Contains("--preflight") && flags.Contains("--wait"))
        {
            throw new SettingsException("Choose --preflight or --wait, not both.");
        }

        var configPath = options.GetValueOrDefault("--config");
        var config = ReadConfig(configPath ?? (File.Exists(DefaultConfigFile) ? DefaultConfigFile : null));

        var site = options.GetValueOrDefault("--site") ?? config?.Site
            ?? throw new SettingsException("Say which site to use, with --site https://...");
        var topic = options.GetValueOrDefault("--topic") ?? config?.Topic
            ?? throw new SettingsException("Say which topic to update, with --topic <code>.");

        if (options.ContainsKey("--until") && options.ContainsKey("--duration"))
        {
            throw new SettingsException("Choose --until or --duration, not both.");
        }

        var until = options.GetValueOrDefault("--until")
            ?? (options.ContainsKey("--duration") ? null : config?.Until);
        var duration = options.GetValueOrDefault("--duration")
            ?? (options.ContainsKey("--until") ? null : config?.Duration);

        return new Settings()
        {
            Site = ParseSite(site),
            Topic = topic.Trim(),
            Mode = flags.Contains("--preflight") ? RunMode.Preflight
                : flags.Contains("--wait") ? RunMode.Wait
                : RunMode.Run,
            Tokens = new TokenSource()
            {
                ConfigPath = config is null ? null : Path.GetFullPath(configPath ?? DefaultConfigFile),
            },
            Model = NullIfBlank(options.GetValueOrDefault("--model") ?? config?.Model),
            Interval = TimeSpan.FromSeconds(
                WholeNumber(options.GetValueOrDefault("--interval"), config?.Interval, 10, 2, "--interval")),
            GenerationTimeout = TimeSpan.FromSeconds(
                WholeNumber(options.GetValueOrDefault("--timeout"), config?.Timeout, 300, 10, "--timeout")),
            EndsAt = EndTime(until, duration),
            ClaudePath = NullIfBlank(options.GetValueOrDefault("--claude") ?? config?.Claude),
            StatePath = Path.GetFullPath(
                options.GetValueOrDefault("--state") ?? config?.State ?? $"whatyousay-runner.{topic.Trim()}.state.json"),
            MaxInputCharacters = WholeNumber(
                options.GetValueOrDefault("--max-input-chars"), config?.MaxInputChars, 400_000, 1000, "--max-input-chars"),
        };
    }

    private static ConfigFile? ReadConfig(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), RunnerJson.Default.ConfigFile)
                ?? throw new SettingsException($"The config file {path} is empty.");
        }
        catch (FileNotFoundException)
        {
            throw new SettingsException($"There is no config file at {Path.GetFullPath(path)}.");
        }
        catch (JsonException problem)
        {
            throw new SettingsException($"The config file {path} is not valid JSON: {problem.Message}");
        }
    }

    private static Uri ParseSite(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var site) || site.Scheme is not ("http" or "https"))
        {
            throw new SettingsException($"--site must be an http or https address, such as https://feedback.example.com. It is \"{value}\".");
        }

        // A trailing slash makes the API paths resolve under a site that lives below the root.
        return site.AbsoluteUri.EndsWith('/') ? site : new Uri(site.AbsoluteUri + "/");
    }

    /// <summary>Reads a whole number from the command line, then the config file, then the default.</summary>
    private static int WholeNumber(string? argument, int? configured, int fallback, int minimum, string name)
    {
        var value = fallback;

        if (argument is not null)
        {
            if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                throw new SettingsException($"{name} must be a whole number. It is \"{argument}\".");
            }
        }
        else if (configured is { } c)
        {
            value = c;
        }

        return value < minimum
            ? throw new SettingsException($"{name} must be at least {minimum}.")
            : value;
    }

    private static DateTimeOffset? EndTime(string? until, string? duration)
    {
        if (duration is not null)
        {
            var match = DurationRegex().Match(duration.Trim());

            if (!match.Success || duration.Trim().Length == 0)
            {
                throw new SettingsException($"--duration must look like 90m, 2h or 1h30m. It is \"{duration}\".");
            }

            var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;

            return DateTimeOffset.Now + new TimeSpan(hours, minutes, 0);
        }

        if (until is null)
        {
            return null;
        }

        if (TimeOnly.TryParse(until, CultureInfo.CurrentCulture, out var time))
        {
            var today = DateTime.Today.Add(time.ToTimeSpan());

            // A time already gone today means tomorrow, for a session that runs past midnight.
            return new DateTimeOffset(today <= DateTime.Now ? today.AddDays(1) : today);
        }

        if (DateTimeOffset.TryParse(until, CultureInfo.CurrentCulture, out var moment))
        {
            return moment <= DateTimeOffset.Now
                ? throw new SettingsException($"--until {until} has already passed.")
                : moment;
        }

        throw new SettingsException($"--until must be a time such as 17:30, or a date and time. It is \"{until}\".");
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [GeneratedRegex(@"^(?:(\d+)h)?\s*(?:(\d+)m)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();
}

/// <summary>The config file's contents. Every field is optional, and the command line wins.</summary>
internal sealed record ConfigFile
{
    public string? Site { get; init; }

    public string? Topic { get; init; }

    public string? Token { get; init; }

    public string? Model { get; init; }

    public int? Interval { get; init; }

    public int? Timeout { get; init; }

    public string? Until { get; init; }

    public string? Duration { get; init; }

    public string? Claude { get; init; }

    public string? State { get; init; }

    public int? MaxInputChars { get; init; }
}

/// <summary>
/// Finds the summariser token for each request. The environment variable wins over the config
/// file. The file is read again each time, so a rotated token written there is picked up
/// without a restart.
/// </summary>
internal sealed record TokenSource
{
    public const string VariableName = "WHATYOUSAY_TOKEN";

    public string? ConfigPath { get; init; }

    /// <summary>Where the token comes from, for messages. Never the token itself.</summary>
    public string Origin =>
        Environment.GetEnvironmentVariable(VariableName) is { Length: > 0 }
            ? $"the {VariableName} environment variable"
            : this.ConfigPath ?? $"the {VariableName} environment variable";

    /// <summary>How to give the runner a regenerated token.</summary>
    public string RotationAdvice =>
        Environment.GetEnvironmentVariable(VariableName) is { Length: > 0 } || this.ConfigPath is null
            ? $"Set {VariableName} to the topic's current token, and start the runner again."
            : "Put the topic's current token in that file. The runner reads it again on every request, so it needs no restart.";

    /// <summary>Returns null when neither source holds a token.</summary>
    public string? Read()
    {
        if (Environment.GetEnvironmentVariable(VariableName) is { Length: > 0 } variable)
        {
            return variable.Trim();
        }

        if (this.ConfigPath is null)
        {
            return null;
        }

        try
        {
            var config = JsonSerializer.Deserialize(File.ReadAllText(this.ConfigPath), RunnerJson.Default.ConfigFile);

            return string.IsNullOrWhiteSpace(config?.Token) ? null : config.Token.Trim();
        }
        catch (Exception problem) when (problem is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// What the runner remembers between runs. It holds no secret: the site, the topic, and the
/// state of the last write. The live summary itself is always read from the site, so a
/// restart revises the same live summary rather than creating another.
/// </summary>
internal sealed record Checkpoint
{
    public required string Site { get; init; }

    public required string Topic { get; init; }

    public Guid? LiveSummaryId { get; init; }

    public int? Revision { get; init; }

    public int? AppliedInputRevision { get; init; }

    public DateTimeOffset? WrittenAt { get; init; }

    /// <summary>A write that was sent but whose answer never arrived. The next start checks whether it landed.</summary>
    public InFlightWrite? InFlight { get; init; }

    /// <summary>How many generations in a row have failed.</summary>
    public int Failures { get; init; }

    /// <summary>The earliest time to generate again after failures, so a restart does not retry straight away.</summary>
    public DateTimeOffset? RetryAt { get; init; }
}

internal sealed record InFlightWrite
{
    public int? BasedOnRevision { get; init; }

    public required int InputRevision { get; init; }

    public required DateTimeOffset SentAt { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(ConfigFile))]
[JsonSerializable(typeof(Checkpoint))]
internal sealed partial class RunnerJson : JsonSerializerContext
{
}

/// <summary>
/// Loads and saves the checkpoint. Holds a lock file beside it, so two runners never share one.
/// </summary>
internal sealed class CheckpointStore : IDisposable
{
    private readonly string mPath;

    private readonly FileStream mLock;

    private CheckpointStore(string path, FileStream lockFile, Checkpoint current)
    {
        mPath = path;
        mLock = lockFile;
        this.Current = current;
    }

    public Checkpoint Current { get; private set; }

    /// <summary>True when the checkpoint came from an earlier run.</summary>
    public bool IsResumed { get; private init; }

    /// <summary>Throws <see cref="IOException"/> when another runner holds the lock.</summary>
    public static CheckpointStore Open(string path, Uri site, string topic)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var lockFile = new FileStream(
            path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);

        var fresh = new Checkpoint()
        {
            Site = site.AbsoluteUri,
            Topic = topic,
        };

        Checkpoint? saved = null;

        try
        {
            saved = File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), RunnerJson.Default.Checkpoint)
                : null;
        }
        catch (JsonException)
        {
            Log.Warn($"The checkpoint at {path} could not be read, so the runner starts afresh.");
        }

        if (saved is not null && (saved.Site != fresh.Site || saved.Topic != topic))
        {
            Log.Warn($"The checkpoint at {path} belongs to another site or topic, so the runner starts afresh.");
            saved = null;
        }

        return new CheckpointStore(path, lockFile, saved ?? fresh)
        {
            IsResumed = saved is not null,
        };
    }

    public void Save(Checkpoint checkpoint)
    {
        this.Current = checkpoint;

        // Written beside the real file and moved over it, so a crash never leaves half a checkpoint.
        var temporary = mPath + ".tmp";

        File.WriteAllText(temporary, JsonSerializer.Serialize(checkpoint, RunnerJson.Default.Checkpoint));
        File.Move(temporary, mPath, overwrite: true);
    }

    public void Dispose()
    {
        mLock.Dispose();
    }
}

/// <summary>The live summary's write state in a snapshot.</summary>
internal sealed record LiveSummaryState
{
    public required Guid Id { get; init; }

    public required int Revision { get; init; }

    public int? AppliedInputRevision { get; init; }
}

/// <summary>The parts of <c>GET /live</c> that the runner acts on, and the input it hands on untouched.</summary>
internal sealed record Snapshot
{
    public required int InputRevision { get; init; }

    public required bool IsPending { get; init; }

    public required bool IsWritable { get; init; }

    public string? NotWritableReason { get; init; }

    public LiveSummaryState? LiveSummary { get; init; }

    public required string Title { get; init; }

    public required int ResponseCount { get; init; }

    /// <summary>The snapshot's <c>input</c>, exactly as the site sent it. It is the model's user message.</summary>
    public required string InputJson { get; init; }

    public required string Instructions { get; init; }

    public required JsonElement OutputSchema { get; init; }

    /// <summary>
    /// Throws <see cref="JsonException"/>, <see cref="KeyNotFoundException"/> or
    /// <see cref="InvalidOperationException"/> when the JSON is not a snapshot.
    /// </summary>
    public static Snapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        var input = root.GetProperty("input");
        var model = root.GetProperty("model");
        var live = root.GetProperty("liveSummary");

        return new Snapshot()
        {
            InputRevision = root.GetProperty("inputRevision").GetInt32(),
            IsPending = root.GetProperty("isPending").GetBoolean(),
            IsWritable = root.GetProperty("isWritable").GetBoolean(),
            NotWritableReason = root.TryGetProperty("notWritableReason", out var reason) ? reason.GetString() : null,
            LiveSummary = live.ValueKind == JsonValueKind.Null
                ? null
                : new LiveSummaryState()
                {
                    Id = live.GetProperty("id").GetGuid(),
                    Revision = live.GetProperty("revision").GetInt32(),
                    AppliedInputRevision = live.TryGetProperty("appliedInputRevision", out var applied)
                        && applied.ValueKind == JsonValueKind.Number
                            ? applied.GetInt32()
                            : null,
                },
            Title = input.GetProperty("topic").GetProperty("title").GetString() ?? string.Empty,
            ResponseCount = input.GetProperty("responses").GetArrayLength(),
            InputJson = input.GetRawText(),
            Instructions = model.GetProperty("instructions").GetString() ?? string.Empty,
            OutputSchema = model.GetProperty("outputSchema").Clone(),
        };
    }

    /// <summary>Whether a write generated from input <paramref name="inputRevision"/> shows in this snapshot.</summary>
    public bool Reflects(int? basedOnRevision, int inputRevision)
    {
        return this.LiveSummary is { } live
            && live.AppliedInputRevision == inputRevision
            && (basedOnRevision is null || live.Revision > basedOnRevision);
    }
}

/// <summary>A result of reading <c>GET /live</c>.</summary>
internal abstract record LiveRead
{
    public sealed record Changed : LiveRead
    {
        public required Snapshot Snapshot { get; init; }

        public string? ETag { get; init; }
    }

    /// <summary>Nothing in the snapshot has changed since the ETag sent.</summary>
    public sealed record Unchanged : LiveRead;

    /// <summary>The site refused the request for a reason a person has to fix, such as a wrong token.</summary>
    public sealed record Refused : LiveRead
    {
        public required string Message { get; init; }
    }

    /// <summary>The site could not be reached, or it answered with a server error. Either can clear up without anyone acting.</summary>
    public sealed record Unreachable : LiveRead
    {
        public required string Message { get; init; }
    }
}

/// <summary>One thing the site found wrong with a write, located by a JSON Pointer into it.</summary>
internal sealed record WriteProblem
{
    public required string Path { get; init; }

    public required string Reason { get; init; }

    public required string Message { get; init; }

    public string? Nearest { get; init; }

    public string? Detail { get; init; }

    /// <summary>The problem as one line for the model, with the nearest matching text when there is one.</summary>
    public override string ToString()
    {
        var text = new StringBuilder($"{this.Path}: {this.Reason}: {this.Message}");

        if (this.Detail is { Length: > 0 } detail)
        {
            text.Append(' ').Append(detail);
        }

        if (this.Nearest is { Length: > 0 } nearest)
        {
            text.Append(" The nearest text in that revision is ").Append(JsonSerializer.Serialize(nearest, RunnerTextJson.Default.String));
        }

        return text.ToString();
    }
}

[JsonSerializable(typeof(string))]
internal sealed partial class RunnerTextJson : JsonSerializerContext
{
}

/// <summary>A result of <c>PUT /live</c>.</summary>
internal abstract record WriteOutcome
{
    public sealed record Accepted : WriteOutcome
    {
        public required int Revision { get; init; }

        public required int AppliedInputRevision { get; init; }

        public required int InputRevision { get; init; }

        public required int NodeCount { get; init; }

        public required int ReferenceCount { get; init; }

        public required bool IsCreated { get; init; }

        public required Guid SummaryId { get; init; }
    }

    /// <summary>A 409: the summary or its access changed since the snapshot. Read again.</summary>
    public sealed record Conflict : WriteOutcome
    {
        public required string Reason { get; init; }

        public required string Detail { get; init; }
    }

    /// <summary>A 400 or 422: the write itself is wrong. <see cref="Problems"/> locates each fault.</summary>
    public sealed record Rejected : WriteOutcome
    {
        public required string Reason { get; init; }

        public required string Detail { get; init; }

        public required IReadOnlyList<WriteProblem> Problems { get; init; }
    }

    public sealed record Refused : WriteOutcome
    {
        public required string Message { get; init; }
    }

    /// <summary>No answer arrived, or the site failed, so the write may or may not have landed.</summary>
    public sealed record Ambiguous : WriteOutcome
    {
        public required string Message { get; init; }
    }
}

/// <summary>
/// Talks to the site's live summary endpoints. The token goes in the Authorization header
/// of each request and nowhere else.
/// </summary>
internal sealed class SiteClient : IDisposable
{
    private readonly HttpClient mHttp;

    private readonly TokenSource mTokens;

    private readonly Uri mLive;

    public SiteClient(Uri site, string topic, TokenSource tokens)
    {
        mTokens = tokens;
        mLive = new Uri(site, $"api/topics/{Uri.EscapeDataString(topic)}/live");

        // Redirects are refused rather than followed, so the token is never sent anywhere but
        // the address the presenter gave.
        mHttp = new HttpClient(new SocketsHttpHandler() { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        mHttp.DefaultRequestHeaders.UserAgent.ParseAdd("WhatYouSay-runner/1");
    }

    public Uri Address =>
        mLive;

    public async Task<LiveRead> ReadAsync(string? etag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, mLive);

        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        if (this.Authorise(request) is { } missing)
        {
            return new LiveRead.Refused() { Message = missing };
        }

        HttpResponseMessage response;

        try
        {
            response = await mHttp.SendAsync(request, cancellationToken);
        }
        catch (Exception failure) when (IsNetworkFailure(failure, cancellationToken))
        {
            return new LiveRead.Unreachable() { Message = Describe(failure) };
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new LiveRead.Unchanged();
            }

            var body = await ReadBodyAsync(response, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                try
                {
                    return new LiveRead.Changed()
                    {
                        Snapshot = Snapshot.Parse(body),
                        ETag = response.Headers.ETag?.ToString(),
                    };
                }
                catch (Exception problem) when (problem is JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    return new LiveRead.Refused()
                    {
                        Message = $"{mLive} answered with something that is not a live snapshot. Check --site, and that the site is up to date.",
                    };
                }
            }

            return IsTransient(response.StatusCode)
                ? new LiveRead.Unreachable() { Message = $"the site answered {(int)response.StatusCode} {response.ReasonPhrase}" }
                : new LiveRead.Refused() { Message = this.Explain(response, body) };
        }
    }

    public async Task<WriteOutcome> WriteAsync(string draftJson, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, mLive)
        {
            Content = new StringContent(draftJson, Encoding.UTF8, "application/json"),
        };

        if (this.Authorise(request) is { } missing)
        {
            return new WriteOutcome.Refused() { Message = missing };
        }

        HttpResponseMessage response;

        try
        {
            response = await mHttp.SendAsync(request, cancellationToken);
        }
        catch (Exception failure) when (IsNetworkFailure(failure, cancellationToken))
        {
            return new WriteOutcome.Ambiguous() { Message = Describe(failure) };
        }

        using (response)
        {
            var body = await ReadBodyAsync(response, cancellationToken);
            var status = (int)response.StatusCode;

            if (status is 200 or 201)
            {
                return Accepted(body, status == 201);
            }

            if (status is 400 or 409 or 422)
            {
                var (reason, detail, problems) = Problem(body);

                return status == 409
                    ? new WriteOutcome.Conflict() { Reason = reason, Detail = detail }
                    : new WriteOutcome.Rejected() { Reason = reason, Detail = detail, Problems = problems };
            }

            // A server error may come after the write committed, so it is as uncertain as no answer at all.
            return IsTransient(response.StatusCode)
                ? new WriteOutcome.Ambiguous() { Message = $"the site answered {status} {response.ReasonPhrase}" }
                : new WriteOutcome.Refused() { Message = this.Explain(response, body) };
        }
    }

    public void Dispose()
    {
        mHttp.Dispose();
    }

    private static WriteOutcome.Accepted Accepted(string body, bool isCreated)
    {
        using var document = JsonDocument.Parse(body);

        var root = document.RootElement;

        return new WriteOutcome.Accepted()
        {
            SummaryId = root.GetProperty("summaryId").GetGuid(),
            Revision = root.GetProperty("revision").GetInt32(),
            AppliedInputRevision = root.GetProperty("appliedInputRevision").GetInt32(),
            InputRevision = root.GetProperty("inputRevision").GetInt32(),
            NodeCount = root.GetProperty("nodeCount").GetInt32(),
            ReferenceCount = root.GetProperty("referenceCount").GetInt32(),
            IsCreated = isCreated,
        };
    }

    /// <summary>Reads a problem details body: its reason, its detail and its located errors.</summary>
    private static (string Reason, string Detail, IReadOnlyList<WriteProblem> Problems) Problem(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            var root = document.RootElement;
            var problems = new List<WriteProblem>();

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var error in errors.EnumerateArray())
                {
                    problems.Add(new WriteProblem()
                    {
                        Path = Text(error, "path") ?? "/",
                        Reason = Text(error, "reason") ?? "invalid",
                        Message = Text(error, "message") ?? string.Empty,
                        Nearest = Text(error, "nearest"),
                        Detail = Text(error, "detail"),
                    });
                }
            }

            return (Text(root, "reason") ?? "unknown", Text(root, "detail") ?? Text(root, "title") ?? string.Empty, problems);
        }
        catch (JsonException)
        {
            return ("unknown", body.Length > 300 ? body[..300] : body, []);
        }
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Whether <paramref name="failure"/> came from the network or a timeout rather than from
    /// the run stopping. A stop is left to propagate.
    /// </summary>
    private static bool IsNetworkFailure(Exception failure, CancellationToken cancellationToken)
    {
        return failure is HttpRequestException
            || (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested);
    }

    private static string Describe(Exception failure)
    {
        return failure is TaskCanceledException
            ? "no answer within 30 seconds"
            : failure.InnerException?.Message ?? failure.Message;
    }

    private static bool IsTransient(HttpStatusCode status)
    {
        return (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
    }

    /// <summary>Adds the token to <paramref name="request"/>. Returns what to do when there is no token.</summary>
    private string? Authorise(HttpRequestMessage request)
    {
        if (mTokens.Read() is not { } token)
        {
            return $"No summariser token. Set {TokenSource.VariableName} to the topic's token, or put it in the config file as \"token\".";
        }

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return null;
    }

    private string Explain(HttpResponseMessage response, string body)
    {
        var (_, detail, _) = Problem(body);

        return (int)response.StatusCode switch
        {
            401 => $"The site refused the token from {mTokens.Origin}: {detail} " + mTokens.RotationAdvice,
            403 => $"{detail} Check --topic, and that the token is this topic's.",
            404 => $"{mLive} does not exist. Check --site and --topic, and that the site is up to date.",
            >= 300 and < 400 => $"The site redirected to {response.Headers.Location}. Use that address in --site.",
            _ => $"The site answered {(int)response.StatusCode} {response.ReasonPhrase}. {detail}",
        };
    }
}

/// <summary>What one generation asks of a model client.</summary>
internal sealed record ModelRequest
{
    /// <summary>The system prompt.</summary>
    public required string Instructions { get; init; }

    /// <summary>The JSON Schema that the answer must match, as JSON text.</summary>
    public required string Schema { get; init; }

    /// <summary>The user message. It holds untrusted text, so it only ever travels by stdin.</summary>
    public required string Message { get; init; }

    public required TimeSpan Timeout { get; init; }
}

/// <summary>A result of one generation.</summary>
internal abstract record ModelReply
{
    public sealed record Answer : ModelReply
    {
        public required JsonNode Json { get; init; }

        public required TimeSpan Duration { get; init; }

        /// <summary>The models the client reported using, when it says.</summary>
        public string? Models { get; init; }
    }

    /// <summary>The model answered, but not with a usable JSON object.</summary>
    public sealed record Invalid : ModelReply
    {
        public required string Problem { get; init; }

        public JsonNode? Json { get; init; }
    }

    /// <summary>The provider's usage or rate limit stopped the generation.</summary>
    public sealed record Limited : ModelReply
    {
        public required string Message { get; init; }

        public DateTimeOffset? RetryAt { get; init; }
    }

    /// <summary>The client could not run, crashed or ran out of time.</summary>
    public sealed record Failed : ModelReply
    {
        public required string Message { get; init; }
    }
}

/// <summary>A model client's own readiness: installed, signed in, and as whom.</summary>
internal sealed record ModelCheck
{
    /// <summary>What to tell the presenter when the client is not ready. Null when it is.</summary>
    public string? Problem { get; init; }

    public required string Description { get; init; }
}

/// <summary>
/// A locally installed model client. Everything specific to one client lives in its
/// implementation: the command, its arguments and how its output is read.
/// </summary>
internal interface IModelClient
{
    string Name { get; }

    string ModelDescription { get; }

    Task<ModelCheck> CheckAsync(CancellationToken cancellationToken);

    Task<ModelReply> GenerateAsync(ModelRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Runs <c>claude -p</c> with every tool turned off, in a temporary working directory of its
/// own so that no project instructions are picked up. Safe mode keeps the presenter's own
/// memory, hooks, plugins and MCP servers out of the generation. The system prompt goes in a
/// file and the input by stdin, so the command line carries only flags, the schema and the model name.
/// </summary>
internal sealed partial class ClaudeCode : IModelClient, IDisposable
{
    private static readonly UTF8Encoding sUtf8 = new(false);

    private readonly string? mConfiguredPath;

    private readonly string? mModel;

    private readonly string mWorkDirectory;

    private string? mExecutable;

    public ClaudeCode(string? configuredPath, string? model, string topic)
    {
        mConfiguredPath = configuredPath;
        mModel = model;
        mWorkDirectory = Path.Combine(Path.GetTempPath(), "whatyousay-runner", $"{topic}-{Environment.ProcessId}");
    }

    public string Name =>
        "Claude Code";

    public string ModelDescription =>
        mModel ?? "Claude Code's default model";

    public async Task<ModelCheck> CheckAsync(CancellationToken cancellationToken)
    {
        if (this.Resolve() is { } missing)
        {
            return new ModelCheck() { Problem = missing, Description = this.Name };
        }

        var version = await this.RunAsync(["--version"], null, TimeSpan.FromSeconds(60), cancellationToken);

        if (version.ExitCode != 0)
        {
            return new ModelCheck()
            {
                Problem = $"{mExecutable} --version failed: {Tail(version.Error)}",
                Description = this.Name,
            };
        }

        var status = await this.RunAsync(["auth", "status", "--json"], null, TimeSpan.FromSeconds(60), cancellationToken);
        var description = $"Claude Code {version.Output.Trim().Replace(" (Claude Code)", string.Empty)} at {mExecutable}";

        JsonNode? account;

        try
        {
            account = JsonNode.Parse(status.Output);
        }
        catch (JsonException)
        {
            // Without a JSON answer from auth status, the preflight generation is what proves the sign-in.
            return new ModelCheck() { Description = $"{description}. Sign-in not checked." };
        }

        if (account?["loggedIn"]?.GetValueKind() != JsonValueKind.True)
        {
            return new ModelCheck()
            {
                Problem = "Claude Code is not signed in. Run claude and sign in with the account to use for the session.",
                Description = description,
            };
        }

        var who = string.Join(", ", new[]
        {
            Value(account, "email"),
            Value(account, "orgName"),
            Value(account, "subscriptionType"),
            Value(account, "authMethod"),
        }.Where(part => part is not null));

        return new ModelCheck() { Description = $"{description}, signed in as {who}" };
    }

    public async Task<ModelReply> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        if (this.Resolve() is { } missing)
        {
            return new ModelReply.Failed() { Message = missing };
        }

        Directory.CreateDirectory(mWorkDirectory);

        var promptFile = Path.Combine(mWorkDirectory, "system-prompt.txt");

        await File.WriteAllTextAsync(promptFile, request.Instructions, sUtf8, cancellationToken);

        List<string> arguments =
        [
            "-p",
            "--output-format", "json",
            "--system-prompt-file", promptFile,
            "--json-schema", WithoutMetaSchema(request.Schema),
            "--tools", string.Empty,
            "--no-session-persistence",
            "--strict-mcp-config",
            "--safe-mode",
        ];

        if (mModel is not null)
        {
            arguments.AddRange(["--model", mModel]);
        }

        var clock = Stopwatch.StartNew();
        var run = await this.RunAsync(arguments, request.Message, request.Timeout, cancellationToken);

        if (run.IsTimedOut)
        {
            return new ModelReply.Failed()
            {
                Message = $"Claude Code took longer than {request.Timeout.TotalSeconds:0} seconds and was stopped. Raise --timeout, or choose a faster --model.",
            };
        }

        return Interpret(run, clock.Elapsed);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(mWorkDirectory))
            {
                Directory.Delete(mWorkDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temporary folder left behind does no harm.
        }
    }

    /// <summary>
    /// Removes the schema's <c>$schema</c> keyword. Claude Code refuses a schema that names
    /// the draft 2020-12 meta-schema, and the keywords the site uses mean the same without it.
    /// </summary>
    private static string WithoutMetaSchema(string schema)
    {
        if (JsonNode.Parse(schema) is not JsonObject root)
        {
            return schema;
        }

        root.Remove("$schema");

        return root.ToJsonString();
    }

    /// <summary>Reads <c>--output-format json</c>: one result object, with the answer in <c>structured_output</c>.</summary>
    private static ModelReply Interpret(ProcessResult run, TimeSpan duration)
    {
        var result = FindResult(run.Output);

        if (result is null)
        {
            var problem = Tail(run.Error.Length > 0 ? run.Error : run.Output);

            if (IsLimit(problem))
            {
                return new ModelReply.Limited() { Message = problem, RetryAt = ResetTime(problem) };
            }

            return new ModelReply.Failed()
            {
                Message = problem.Contains("unknown option", StringComparison.OrdinalIgnoreCase)
                    ? $"{problem} This runner needs a newer Claude Code. Update it with: claude update"
                    : $"Claude Code exited with code {run.ExitCode} and no result. {problem}",
            };
        }

        var text = Value(result, "result");
        var subtype = Value(result, "subtype");
        var isError = result["is_error"]?.GetValueKind() == JsonValueKind.True;

        if (isError || subtype != "success")
        {
            var message = text ?? subtype ?? "Claude Code reported an error.";

            if (subtype == "error_max_structured_output_retries")
            {
                return new ModelReply.Invalid() { Problem = "The answer did not match the output schema." };
            }

            return IsLimit(message) || Value(result, "api_error_status") is "429" or "529"
                ? new ModelReply.Limited() { Message = message, RetryAt = ResetTime(message) }
                : new ModelReply.Failed() { Message = message };
        }

        var answer = result["structured_output"] ?? ParseLoosely(text);

        if (answer is not JsonObject)
        {
            return new ModelReply.Invalid() { Problem = "The answer was not a JSON object." };
        }

        var models = result["modelUsage"] is JsonObject usage ? string.Join(", ", usage.Select(entry => entry.Key)) : null;

        return new ModelReply.Answer()
        {
            Json = answer.DeepClone(),
            Duration = duration,
            Models = models,
        };
    }

    /// <summary>Finds the result object, even if something else was printed around it.</summary>
    private static JsonObject? FindResult(string output)
    {
        var candidates = new[] { output.Trim() }
            .Concat(output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse());

        foreach (var candidate in candidates)
        {
            if (!candidate.StartsWith('{'))
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(candidate) is JsonObject node && Value(node, "type") == "result")
                {
                    return node;
                }
            }
            catch (JsonException)
            {
                // Not the result line.
            }
        }

        return null;
    }

    /// <summary>Reads an answer given as text, with or without a markdown code fence around it.</summary>
    private static JsonNode? ParseLoosely(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = FenceRegex().Replace(text.Trim(), string.Empty);

        try
        {
            return JsonNode.Parse(trimmed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsLimit(string message)
    {
        return LimitRegex().IsMatch(message);
    }

    /// <summary>Some limit messages end with the reset time as Unix seconds after a bar.</summary>
    private static DateTimeOffset? ResetTime(string message)
    {
        var match = ResetRegex().Match(message);

        return match.Success && long.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static string? Value(JsonNode? node, string name)
    {
        return node?[name] is JsonValue value
            ? value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.Number => value.ToJsonString(),
                _ => null,
            }
            : null;
    }

    private static string Tail(string text)
    {
        var trimmed = text.Trim();

        return trimmed.Length > 500 ? "..." + trimmed[^500..] : trimmed;
    }

    [GeneratedRegex(@"usage limit|rate limit|rate_limit|limit reached|hit your limit|overloaded|quota|\b429\b|\b529\b", RegexOptions.IgnoreCase)]
    private static partial Regex LimitRegex();

    [GeneratedRegex(@"\|(\d{10})\b")]
    private static partial Regex ResetRegex();

    [GeneratedRegex(@"^```(?:json)?\s*|\s*```$")]
    private static partial Regex FenceRegex();

    /// <summary>
    /// Finds the executable. Returns what to do when it cannot be found. A Windows batch
    /// shim is refused, because cmd.exe would reinterpret the arguments.
    /// </summary>
    private string? Resolve()
    {
        if (mExecutable is not null)
        {
            return null;
        }

        if (mConfiguredPath is not null)
        {
            if (!File.Exists(mConfiguredPath))
            {
                return $"There is no file at {mConfiguredPath}. Check --claude.";
            }

            mExecutable = Path.GetFullPath(mConfiguredPath);

            return IsBatchFile(mExecutable) ? BatchProblem(mExecutable) : null;
        }

        var windows = OperatingSystem.IsWindows();
        var name = windows ? "claude.exe" : "claude";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(Path.Combine(home, ".local", "bin"))
            .Append(Path.Combine(home, ".claude", "local"));

        foreach (var folder in folders)
        {
            var candidate = Path.Combine(folder.Trim('"'), name);

            if (File.Exists(candidate))
            {
                mExecutable = candidate;

                return null;
            }

            if (windows && File.Exists(Path.Combine(folder.Trim('"'), "claude.cmd")))
            {
                return BatchProblem(Path.Combine(folder, "claude.cmd"));
            }
        }

        return "Claude Code is not installed, or not on PATH. Install it from https://code.claude.com/docs/en/setup, "
            + "or give its location with --claude.";
    }

    private static bool IsBatchFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".cmd" or ".bat";
    }

    private static string BatchProblem(string path)
    {
        return $"Found {path}, a batch file. The runner needs the claude.exe that the native installer puts in "
            + "%USERPROFILE%\\.local\\bin. Install it from https://code.claude.com/docs/en/setup, or pass its path with --claude.";
    }

    /// <summary>
    /// Runs the executable with each argument passed as it is, never through a shell, and
    /// writes <paramref name="input"/> to its stdin. Kills the whole process tree on timeout or cancellation.
    /// </summary>
    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? input,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(mWorkDirectory);

        var start = new ProcessStartInfo(mExecutable!)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = sUtf8,
            StandardOutputEncoding = sUtf8,
            StandardErrorEncoding = sUtf8,
            CreateNoWindow = true,
            WorkingDirectory = mWorkDirectory,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The model client has no use for the topic token, so it never inherits it.
        start.Environment.Remove(TokenSource.VariableName);

        using var process = new Process() { StartInfo = start };

        try
        {
            process.Start();
        }
        catch (Win32Exception failure)
        {
            return new ProcessResult() { ExitCode = -1, Output = string.Empty, Error = $"Could not start {mExecutable}: {failure.Message}" };
        }

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        limit.CancelAfter(timeout);

        try
        {
            try
            {
                if (input is not null)
                {
                    await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token);
                }

                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The process exited before reading all of its input. Its output says why.
            }

            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult() { ExitCode = -1, Output = string.Empty, Error = string.Empty, IsTimedOut = true };
        }

        return new ProcessResult()
        {
            ExitCode = process.ExitCode,
            Output = await output,
            Error = await error,
        };
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (InvalidOperationException)
        {
            // It had already exited.
        }
    }
}

internal sealed record ProcessResult
{
    public required int ExitCode { get; init; }

    public required string Output { get; init; }

    public required string Error { get; init; }

    public bool IsTimedOut { get; init; }
}

/// <summary>
/// Checks a JSON value against the parts of JSON Schema that the site's output schema uses:
/// type, required, properties, additionalProperties, items, minItems, minLength, minimum,
/// anyOf, local $ref and the uuid format. Other keywords are ignored, and the site checks
/// everything again.
/// </summary>
internal static class SchemaCheck
{
    /// <summary>Returns each problem as a JSON Pointer and a message. Empty when the value matches.</summary>
    public static List<string> Validate(JsonElement schema, JsonNode? value)
    {
        var problems = new List<string>();

        Check(schema, schema, value, string.Empty, problems);

        return problems;
    }

    private static void Check(JsonElement root, JsonElement schema, JsonNode? value, string path, List<string> problems)
    {
        var at = path.Length == 0 ? "/" : path;

        if (schema.TryGetProperty("$ref", out var reference))
        {
            schema = Resolve(root, reference.GetString() ?? string.Empty);
        }

        if (schema.TryGetProperty("type", out var type) && type.GetString() is { } expected && !IsType(value, expected))
        {
            problems.Add($"{at}: should be {Article(expected)} {expected}.");

            return;
        }

        if (schema.TryGetProperty("anyOf", out var anyOf))
        {
            var branches = anyOf.EnumerateArray()
                .Select(branch =>
                {
                    var found = new List<string>();

                    Check(root, branch, value, path, found);

                    return found;
                })
                .ToList();

            if (branches.All(found => found.Count > 0))
            {
                problems.Add($"{at}: should match one of these: {string.Join(" Or: ", branches.Select(found => string.Join(" ", found)))}");
            }
        }

        switch (value)
        {
            case JsonObject obj:
                CheckObject(root, schema, obj, path, problems);
                break;

            case JsonArray array:
                CheckArray(root, schema, array, path, problems);
                break;

            case JsonValue scalar:
                CheckScalar(schema, scalar, at, problems);
                break;
        }
    }

    private static void CheckObject(JsonElement root, JsonElement schema, JsonObject obj, string path, List<string> problems)
    {
        var at = path.Length == 0 ? "/" : path;

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(n => n.GetString()!))
            {
                if (!obj.ContainsKey(name))
                {
                    problems.Add($"{at}: \"{name}\" is required.");
                }
            }
        }

        var properties = schema.TryGetProperty("properties", out var declared) ? declared : default;
        var closed = schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False;

        foreach (var (name, child) in obj)
        {
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(name, out var childSchema))
            {
                Check(root, childSchema, child, $"{path}/{Escape(name)}", problems);
            }
            else if (closed)
            {
                problems.Add($"{at}: \"{name}\" is not allowed here.");
            }
        }
    }

    private static void CheckArray(JsonElement root, JsonElement schema, JsonArray array, string path, List<string> problems)
    {
        if (schema.TryGetProperty("minItems", out var minItems) && array.Count < minItems.GetInt32())
        {
            problems.Add($"{(path.Length == 0 ? "/" : path)}: needs at least {minItems.GetInt32()} item(s).");
        }

        if (schema.TryGetProperty("items", out var items))
        {
            for (var i = 0; i < array.Count; i++)
            {
                Check(root, items, array[i], $"{path}/{i}", problems);
            }
        }
    }

    private static void CheckScalar(JsonElement schema, JsonValue scalar, string at, List<string> problems)
    {
        if (scalar.GetValueKind() == JsonValueKind.String)
        {
            var text = scalar.GetValue<string>();

            if (schema.TryGetProperty("minLength", out var minLength) && text.Length < minLength.GetInt32())
            {
                problems.Add($"{at}: should not be empty.");
            }

            if (schema.TryGetProperty("format", out var format) && format.GetString() == "uuid" && !Guid.TryParse(text, out _))
            {
                problems.Add($"{at}: should be a uuid.");
            }
        }
        else if (scalar.GetValueKind() == JsonValueKind.Number
            && schema.TryGetProperty("minimum", out var minimum)
            && scalar.GetValue<double>() < minimum.GetDouble())
        {
            problems.Add($"{at}: should be at least {minimum.GetRawText()}.");
        }
    }

    private static bool IsType(JsonNode? value, string type)
    {
        var kind = value?.GetValueKind() ?? JsonValueKind.Null;

        return type switch
        {
            "object" => kind == JsonValueKind.Object,
            "array" => kind == JsonValueKind.Array,
            "string" => kind == JsonValueKind.String,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            "null" => kind == JsonValueKind.Null,
            "number" => kind == JsonValueKind.Number,
            "integer" => kind == JsonValueKind.Number && value!.AsValue().TryGetValue<long>(out _),
            _ => true,
        };
    }

    private static JsonElement Resolve(JsonElement root, string reference)
    {
        var target = root;

        foreach (var part in reference.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            target = target.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
        }

        return target;
    }

    private static string Escape(string name)
    {
        return name.Replace("~", "~0").Replace("/", "~1");
    }

    private static string Article(string type)
    {
        return type is "object" or "array" or "integer" ? "an" : "a";
    }
}

/// <summary>How one generation pass ended.</summary>
internal enum PassResult
{
    /// <summary>The site accepted the summary.</summary>
    Written,

    /// <summary>The snapshot is out of date. Read again before anything else.</summary>
    Stale,

    /// <summary>The pass failed. Wait before trying again.</summary>
    Failed,

    /// <summary>The provider's limit stopped it. Wait longer before trying again.</summary>
    Limited,
}

/// <summary>
/// The polling loop. Checks the topic every interval with a conditional read, and generates
/// only when the input has changed and the live summary is writable. Runs one generation at
/// a time: input that arrives meanwhile is picked up by the read straight after it.
/// </summary>
internal sealed class LiveRunner
{
    private static readonly TimeSpan sFirstFailureDelay = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan sLongestFailureDelay = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan sFirstLimitDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan sLongestLimitDelay = TimeSpan.FromMinutes(15);

    private const int StaleLimit = 3;

    private const int WriteAttempts = 3;

    private readonly Settings mSettings;

    private readonly SiteClient mSite;

    private readonly IModelClient mModel;

    private readonly CheckpointStore mStore;

    private readonly Shutdown mShutdown;

    private string? mETag;

    private Snapshot? mSnapshot;

    private string? mLastState;

    private string? mLastProblem;

    private DateTimeOffset? mOutageSince;

    private int mStaleInARow;

    private int mOversizedRevision;

    public LiveRunner(Settings settings, SiteClient site, IModelClient model, CheckpointStore store, Shutdown shutdown)
    {
        mSettings = settings;
        mSite = site;
        mModel = model;
        mStore = store;
        mShutdown = shutdown;
    }

    private CancellationToken Stopping =>
        mShutdown.Token;

    public async Task<int> RunAsync()
    {
        try
        {
            if (!await this.CheckAsync())
            {
                return ExitCodes.Failed;
            }

            if (mSettings.Mode == RunMode.Preflight)
            {
                return await this.PreflightGenerationAsync() ? ExitCodes.Done : ExitCodes.Failed;
            }

            await this.LoopAsync();
        }
        catch (OperationCanceledException) when (this.Stopping.IsCancellationRequested)
        {
            // Stopping is the normal way out of the loop.
        }

        Log.Info($"Stopped, because {mShutdown.Reason}. The live summary stays as it was last written.");

        return ExitCodes.Done;
    }

    /// <summary>
    /// Checks the site, the token, the topic and the model client, and prints what it found.
    /// Returns false when the runner cannot work. A topic that is paused or under review only
    /// fails the preflight, because an admin can change it during a run.
    /// </summary>
    private async Task<bool> CheckAsync()
    {
        var preflight = mSettings.Mode == RunMode.Preflight;
        var ok = true;

        Log.Info($"Checking {mSite.Address}");

        switch (await mSite.ReadAsync(null, this.Stopping))
        {
            case LiveRead.Changed changed:
                mSnapshot = changed.Snapshot;
                mETag = changed.ETag;
                Log.Good($"Site: \"{mSnapshot.Title}\", {mSnapshot.ResponseCount} response(s), input revision {mSnapshot.InputRevision}.");
                break;

            case LiveRead.Refused refused:
                Log.Error($"Site: {refused.Message}");
                ok = false;
                break;

            case LiveRead.Unreachable unreachable:
                Log.Error($"Site: cannot reach it ({unreachable.Message}). Check --site, and that this computer can open it in a browser.");
                ok = false;
                break;

            default:
                ok = false;
                break;
        }

        if (mSnapshot is { } snapshot)
        {
            this.ReportResumption(snapshot);

            if (!snapshot.IsWritable)
            {
                var message = $"Topic: {NotWritable(snapshot.NotWritableReason)}";

                if (preflight)
                {
                    Log.Error(message);
                    ok = false;
                }
                else
                {
                    Log.Warn(message);
                }
            }
            else
            {
                mLastState = Describe(snapshot);
                Log.Good($"Topic: live and writable. {mLastState}");
            }

            if (snapshot.InputJson.Length > mSettings.MaxInputCharacters)
            {
                Log.Error(this.Oversized(snapshot));
                ok = false;
            }
        }

        var check = await mModel.CheckAsync(this.Stopping);

        if (check.Problem is { } problem)
        {
            Log.Error($"{mModel.Name}: {problem}");
            ok = false;
        }
        else
        {
            Log.Good($"{mModel.Name}: {check.Description}. Model: {mModel.ModelDescription}.");
        }

        if (!ok)
        {
            Log.Error(preflight ? "Preflight failed. Fix the problems above and run it again." : "Not starting. Fix the problems above.");
        }

        return ok;
    }

    /// <summary>
    /// Asks the model for a tiny answer in the site's output schema. That proves the sign-in,
    /// the model, the flags and the schema all work, without the cost of a real summary.
    /// The answer is checked but never written.
    /// </summary>
    private async Task<bool> PreflightGenerationAsync()
    {
        Log.Info($"Asking {mModel.Name} for a tiny test answer with {mModel.ModelDescription}...");

        var schema = mSnapshot!.OutputSchema;
        var reply = await mModel.GenerateAsync(
            new ModelRequest()
            {
                Instructions = "You are checking that a summariser works. Answer in the output schema, as briefly as it allows.",
                Schema = schema.GetRawText(),
                Message = "Preflight check. Answer with the body \"ok\" and one node whose text is \"ok\", with no references.",
                Timeout = mSettings.GenerationTimeout,
            },
            this.Stopping);

        switch (reply)
        {
            case ModelReply.Answer answer when SchemaCheck.Validate(schema, answer.Json) is { Count: > 0 } problems:
                Log.Error($"{mModel.Name} answered, but not in the output schema: {string.Join(" ", problems)}");
                break;

            case ModelReply.Answer answer:
                Log.Good($"Test answer arrived in {answer.Duration.TotalSeconds:0.0} s from {answer.Models ?? "the default model"}.");
                Log.Good("Preflight passed. Start the runner without --preflight when the session begins.");

                return true;

            case ModelReply.Limited limited:
                Log.Error($"{mModel.Name} hit a usage limit: {limited.Message}");
                break;

            case ModelReply.Invalid invalid:
                Log.Error($"{mModel.Name} answered, but not as asked: {invalid.Problem}");
                break;

            case ModelReply.Failed failed:
                Log.Error($"{mModel.Name}: {failed.Message}");
                break;
        }

        Log.Error("Preflight failed. Fix the problem above and run it again.");

        return false;
    }

    private async Task LoopAsync()
    {
        var ends = mSettings.EndsAt is { } end ? $", until {end:ddd HH:mm}" : ", until Ctrl+C";

        Log.Info($"Watching topic {mSettings.Topic} every {mSettings.Interval.TotalSeconds:0} s{ends}.");

        while (true)
        {
            this.Stopping.ThrowIfCancellationRequested();

            if (mSnapshot is { } snapshot && this.ShouldGenerate(snapshot))
            {
                var result = await this.PassAsync(snapshot);

                if (result is PassResult.Written or PassResult.Stale)
                {
                    // Read again straight away: input that arrived during the pass is waiting.
                    await this.PollAsync();

                    continue;
                }
            }

            await Task.Delay(mSettings.Interval, this.Stopping);
            await this.PollAsync();
        }
    }

    /// <summary>Reads the snapshot, conditionally on the last ETag, and reports what changed.</summary>
    private async Task PollAsync()
    {
        var read = await mSite.ReadAsync(mETag, this.Stopping);

        switch (read)
        {
            case LiveRead.Changed changed:
                this.Recovered();
                mSnapshot = changed.Snapshot;
                mETag = changed.ETag;
                this.ReportState(changed.Snapshot);
                break;

            case LiveRead.Unchanged:
                this.Recovered();
                break;

            case LiveRead.Refused refused:
                this.Problem(refused.Message);
                break;

            case LiveRead.Unreachable unreachable:
                mOutageSince ??= DateTimeOffset.Now;
                this.Problem($"Cannot reach the site ({unreachable.Message}). The summary on screen stays as it is. Still trying.");
                break;
        }
    }

    private bool ShouldGenerate(Snapshot snapshot)
    {
        if (!snapshot.IsWritable || !snapshot.IsPending || mLastProblem is not null)
        {
            return false;
        }

        if (snapshot.InputJson.Length > mSettings.MaxInputCharacters)
        {
            if (mOversizedRevision != snapshot.InputRevision)
            {
                mOversizedRevision = snapshot.InputRevision;
                Log.Error(this.Oversized(snapshot));
            }

            return false;
        }

        var retryAt = mStore.Current.RetryAt;

        return retryAt is null || DateTimeOffset.Now >= retryAt;
    }

    /// <summary>One generation and write, with at most one repair of an invalid answer.</summary>
    private async Task<PassResult> PassAsync(Snapshot snapshot)
    {
        var waiting = snapshot.LiveSummary?.AppliedInputRevision is { } applied
            ? $"{snapshot.InputRevision - applied} change(s) since the last summary"
            : snapshot.LiveSummary is null ? "no live summary yet" : "the last summary's input is unknown";

        Log.Info($"Generating from input revision {snapshot.InputRevision} ({waiting}) with {mModel.ModelDescription}...");

        var request = new ModelRequest()
        {
            Instructions = snapshot.Instructions,
            Schema = snapshot.OutputSchema.GetRawText(),
            Message = snapshot.InputJson,
            Timeout = mSettings.GenerationTimeout,
        };

        var reply = await mModel.GenerateAsync(request, this.Stopping);
        var repaired = false;

        while (true)
        {
            IReadOnlyList<string> problems;
            JsonNode? rejected;

            switch (reply)
            {
                case ModelReply.Limited limited:
                    return this.Limited(limited);

                case ModelReply.Failed failed:
                    return this.Failed(failed.Message);

                case ModelReply.Invalid invalid:
                    problems = [invalid.Problem];
                    rejected = invalid.Json;
                    break;

                case ModelReply.Answer answer:
                    var found = SchemaCheck.Validate(snapshot.OutputSchema, answer.Json);

                    if (found.Count == 0)
                    {
                        Log.Info($"Answer arrived in {answer.Duration.TotalSeconds:0.0} s{(answer.Models is { } m ? $" from {m}" : string.Empty)}.");

                        var written = await this.WriteAsync(snapshot, answer.Json);

                        if (written.Problems is null)
                        {
                            return written.Result;
                        }

                        problems = written.Problems;
                    }
                    else
                    {
                        problems = found;
                    }

                    rejected = answer.Json;
                    break;

                default:
                    return this.Failed("The model client gave no answer.");
            }

            Log.Warn($"The answer was not accepted, with {problems.Count} problem(s):");

            // The model gets every problem in full. The console gets enough to see what went wrong.
            foreach (var problem in problems.Take(5))
            {
                Log.Detail(problem.Length > 200 ? problem[..200] + "..." : problem);
            }

            if (repaired)
            {
                return this.Failed("The repaired answer was not accepted either.");
            }

            Log.Info("Asking the model to fix them, once.");
            repaired = true;
            reply = await mModel.GenerateAsync(request with { Message = RepairMessage(snapshot, rejected, problems) }, this.Stopping);
        }
    }

    /// <summary>
    /// Writes an answer, generated from <paramref name="snapshot"/>, to the live summary.
    /// Returns problems for a repair when the site rejected the answer itself.
    /// </summary>
    private async Task<(PassResult Result, IReadOnlyList<string>? Problems)> WriteAsync(Snapshot snapshot, JsonNode answer)
    {
        var draft = answer.DeepClone().AsObject();
        var basedOn = snapshot.LiveSummary?.Revision;

        // A null basedOnRevision creates the live summary, and the site refuses it when one
        // already exists. So a write never makes a second live summary.
        draft["basedOnRevision"] = basedOn;
        draft["inputRevision"] = snapshot.InputRevision;

        var json = draft.ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            mStore.Save(mStore.Current with
            {
                InFlight = new InFlightWrite()
                {
                    BasedOnRevision = basedOn,
                    InputRevision = snapshot.InputRevision,
                    SentAt = DateTimeOffset.Now,
                },
            });

            var outcome = await mSite.WriteAsync(json, this.Stopping);

            if (outcome is not WriteOutcome.Ambiguous ambiguous)
            {
                mStore.Save(mStore.Current with { InFlight = null });

                return this.Judge(outcome);
            }

            Log.Warn($"No clear answer to the write ({ambiguous.Message}). Checking whether it landed before trying again.");

            var current = await this.ReadWhenReachableAsync();

            mStore.Save(mStore.Current with { InFlight = null });

            if (current is null)
            {
                return (this.Failed("The site refused the check. The summary may not have been written."), null);
            }

            if (current.Reflects(basedOn, snapshot.InputRevision))
            {
                Log.Good($"It landed: live summary revision {current.LiveSummary!.Revision}.");
                this.Succeeded(current.LiveSummary, current.LiveSummary.Revision);

                return (PassResult.Written, null);
            }

            if (attempt >= WriteAttempts || current.LiveSummary?.Revision != basedOn || !current.IsWritable)
            {
                Log.Info("It did not land, and the live summary has changed since. Reading again.");

                return (PassResult.Stale, null);
            }

            Log.Info("It did not land. Sending it again.");
        }
    }

    /// <summary>Turns a definite write outcome into the pass result, or problems for a repair.</summary>
    private (PassResult Result, IReadOnlyList<string>? Problems) Judge(WriteOutcome outcome)
    {
        switch (outcome)
        {
            case WriteOutcome.Accepted accepted:
                var waiting = accepted.InputRevision - accepted.AppliedInputRevision;

                Log.Good(
                    $"{(accepted.IsCreated ? "Created" : "Updated")} the live summary: revision {accepted.Revision}, "
                    + $"{accepted.NodeCount} node(s), {accepted.ReferenceCount} quote(s), from input revision {accepted.AppliedInputRevision}."
                    + (waiting > 0 ? $" {waiting} newer change(s) waiting." : string.Empty));

                this.Succeeded(
                    new LiveSummaryState()
                    {
                        Id = accepted.SummaryId,
                        Revision = accepted.Revision,
                        AppliedInputRevision = accepted.AppliedInputRevision,
                    },
                    accepted.Revision);

                return (PassResult.Written, null);

            case WriteOutcome.Conflict conflict:
                Log.Info($"The live summary changed while generating ({conflict.Reason}), so this answer is discarded. Reading again.");

                return (this.Stale(), null);

            case WriteOutcome.Rejected rejected when MustReadAgain(rejected):
                Log.Info($"The input changed under this answer ({Reasons(rejected)}), so it is discarded. Reading again.");

                return (this.Stale(), null);

            case WriteOutcome.Rejected rejected:
                var problems = rejected.Problems.Count > 0
                    ? rejected.Problems.Select(problem => problem.ToString()).ToList()
                    : [$"{rejected.Reason}: {rejected.Detail}"];

                return (PassResult.Failed, problems);

            case WriteOutcome.Refused refused:
                return (this.Failed(refused.Message), null);

            default:
                return (this.Failed("The write had no outcome."), null);
        }
    }

    /// <summary>
    /// Whether a rejection is about the snapshot rather than the answer: a quoted response was
    /// withdrawn or hidden, or another write recorded newer input.
    /// </summary>
    private static bool MustReadAgain(WriteOutcome.Rejected rejected)
    {
        return rejected.Reason is "input_revision_out_of_range" or "input_revision_required"
            || rejected.Problems.Any(problem => problem.Reason is "unknown_response" or "input_revision_out_of_range");
    }

    private static string Reasons(WriteOutcome.Rejected rejected)
    {
        return string.Join(", ", rejected.Problems.Select(problem => problem.Reason).Append(rejected.Reason).Distinct());
    }

    /// <summary>
    /// The user message for a repair: the problems, then the same input and the rejected
    /// answer as JSON. It goes by stdin like the first message.
    /// </summary>
    private static string RepairMessage(Snapshot snapshot, JsonNode? rejected, IReadOnlyList<string> problems)
    {
        var text = new StringBuilder();

        text.AppendLine(
            "Your previous answer was not accepted. Return a corrected, complete answer for the same input. "
            + "Fix every problem listed. Each problem starts with a JSON Pointer into your previous answer.");
        text.AppendLine();

        foreach (var problem in problems)
        {
            text.Append("- ").AppendLine(problem);
        }

        var payload = new JsonObject()
        {
            ["input"] = JsonNode.Parse(snapshot.InputJson),
            ["previousAnswer"] = rejected?.DeepClone(),
        };

        text.AppendLine();
        text.AppendLine("The input, and your previous answer:");
        text.Append(payload.ToJsonString());

        return text.ToString();
    }

    /// <summary>Reads a fresh snapshot, waiting through outages. Returns null when the site refuses.</summary>
    private async Task<Snapshot?> ReadWhenReachableAsync()
    {
        while (true)
        {
            switch (await mSite.ReadAsync(null, this.Stopping))
            {
                case LiveRead.Changed changed:
                    this.Recovered();
                    mSnapshot = changed.Snapshot;
                    mETag = changed.ETag;

                    return changed.Snapshot;

                case LiveRead.Refused refused:
                    this.Problem(refused.Message);

                    return null;

                case LiveRead.Unreachable unreachable:
                    mOutageSince ??= DateTimeOffset.Now;
                    this.Problem($"Cannot reach the site ({unreachable.Message}). Still trying.");
                    await Task.Delay(mSettings.Interval, this.Stopping);
                    break;
            }
        }
    }

    private void Succeeded(LiveSummaryState live, int revision)
    {
        mStaleInARow = 0;
        mStore.Save(mStore.Current with
        {
            LiveSummaryId = live.Id,
            Revision = revision,
            AppliedInputRevision = live.AppliedInputRevision,
            WrittenAt = DateTimeOffset.Now,
            Failures = 0,
            RetryAt = null,
        });
    }

    private PassResult Stale()
    {
        // A conflict every time means something else keeps writing. Back off rather than spin.
        return ++mStaleInARow >= StaleLimit
            ? this.Failed("The live summary keeps changing under each answer. Is another runner writing to this topic?")
            : PassResult.Stale;
    }

    private PassResult Failed(string message)
    {
        mStaleInARow = 0;

        var failures = mStore.Current.Failures + 1;
        var delay = Backoff(sFirstFailureDelay, sLongestFailureDelay, failures);
        var retryAt = DateTimeOffset.Now + delay;

        mStore.Save(mStore.Current with { Failures = failures, RetryAt = retryAt });
        Log.Error(message);
        Log.Detail($"The summary on screen stays as it is. Trying again after {retryAt:HH:mm:ss}.");

        return PassResult.Failed;
    }

    private PassResult Limited(ModelReply.Limited limited)
    {
        var failures = mStore.Current.Failures + 1;
        var retryAt = limited.RetryAt is { } reset && reset > DateTimeOffset.Now
            ? reset
            : DateTimeOffset.Now + Backoff(sFirstLimitDelay, sLongestLimitDelay, failures);

        mStore.Save(mStore.Current with { Failures = failures, RetryAt = retryAt });
        Log.Error($"{mModel.Name} hit a usage or rate limit: {limited.Message}");
        Log.Detail($"The summary on screen stays as it is. Trying again after {retryAt:HH:mm:ss}.");

        return PassResult.Limited;
    }

    private static TimeSpan Backoff(TimeSpan first, TimeSpan longest, int failures)
    {
        var factor = Math.Pow(2, Math.Min(failures - 1, 10));

        return TimeSpan.FromTicks(Math.Min(longest.Ticks, (long)(first.Ticks * factor)));
    }

    /// <summary>Prints a problem once, rather than on every poll while it lasts.</summary>
    private void Problem(string message)
    {
        if (message != mLastProblem)
        {
            Log.Error(message);
            mLastProblem = message;
        }
    }

    private void Recovered()
    {
        if (mLastProblem is null)
        {
            return;
        }

        Log.Good(mOutageSince is { } since
            ? $"The site is answering again after {(DateTimeOffset.Now - since).TotalSeconds:0} s."
            : "The site is accepting the runner's requests again.");
        mLastProblem = null;
        mOutageSince = null;
    }

    private void ReportState(Snapshot snapshot)
    {
        var state = snapshot.IsWritable ? Describe(snapshot) : NotWritable(snapshot.NotWritableReason);

        if (state != mLastState)
        {
            mLastState = state;

            if (snapshot.IsWritable)
            {
                Log.Info(state);
            }
            else
            {
                Log.Warn(state);
            }
        }
    }

    /// <summary>Says what an earlier run left behind: its last write, and a write it never heard back about.</summary>
    private void ReportResumption(Snapshot snapshot)
    {
        if (!mStore.IsResumed)
        {
            return;
        }

        var saved = mStore.Current;

        Log.Info($"Resuming from {mSettings.StatePath}.");

        if (saved.LiveSummaryId is { } id && snapshot.LiveSummary?.Id is { } now && now != id)
        {
            Log.Warn($"The live summary is now {now}, not {id}. An admin made another summary live, so the runner follows it.");
        }

        if (saved.InFlight is { } inFlight)
        {
            Log.Info(snapshot.Reflects(inFlight.BasedOnRevision, inFlight.InputRevision)
                ? "The write the last run was waiting on had landed."
                : "The write the last run was waiting on had not landed. The runner generates again when input is waiting.");
            mStore.Save(saved with { InFlight = null });
        }

        if (saved.RetryAt is { } retryAt && retryAt > DateTimeOffset.Now)
        {
            Log.Warn($"The last run had failures, so the runner waits until {retryAt:HH:mm:ss} before generating. Delete the state file to skip that.");
        }
    }

    private string Oversized(Snapshot snapshot)
    {
        return $"The topic's input is {snapshot.InputJson.Length:N0} characters, more than the {mSettings.MaxInputCharacters:N0} "
            + "this runner sends to a model. Feedback is never trimmed, so nothing is generated. "
            + "Raise --max-input-chars if the model has room for it.";
    }

    private static string Describe(Snapshot snapshot)
    {
        var live = snapshot.LiveSummary;

        var summary = live switch
        {
            null => "No live summary yet",
            { AppliedInputRevision: { } applied } => $"Live summary revision {live.Revision}, from input revision {applied}",
            _ => $"Live summary revision {live.Revision}, from unknown input",
        };

        var pending = snapshot.IsPending ? "new input waiting" : "up to date";

        return $"Input revision {snapshot.InputRevision}, {snapshot.ResponseCount} response(s). {summary}: {pending}.";
    }

    private static string NotWritable(string? reason)
    {
        return reason switch
        {
            "paused" => "An admin has paused the summariser. Waiting for them to resume it on the admin page.",
            "reviewed" => "The topic requires review, so it has no live summary to update. Waiting for an admin to choose \"Publish updates live\" on the admin page.",
            _ => $"The live summary cannot be written now ({reason ?? "no reason given"}). Waiting.",
        };
    }
}

/// <summary>
/// The --wait mode. Polls until the topic has input waiting for a summary, then exits, so an
/// interactive session can run it in the background and wake when there is work to do.
/// </summary>
internal sealed class Waiter
{
    private readonly Settings mSettings;

    private readonly SiteClient mSite;

    public Waiter(Settings settings, SiteClient site)
    {
        mSettings = settings;
        mSite = site;
    }

    public async Task<int> RunAsync(CancellationToken stopping)
    {
        string? etag = null;
        string? lastProblem = null;
        var paused = false;

        try
        {
            while (true)
            {
                switch (await mSite.ReadAsync(etag, stopping))
                {
                    case LiveRead.Changed { Snapshot: { IsPending: true, IsWritable: true } snapshot }:
                        Log.Plain(
                            $"Input waiting: input revision {snapshot.InputRevision}, live summary "
                            + (snapshot.LiveSummary is { } live ? $"revision {live.Revision} from input {live.AppliedInputRevision?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}" : "not created yet")
                            + $". Read {mSite.Address} and write the next version.");

                        return ExitCodes.Done;

                    case LiveRead.Changed changed:
                        etag = changed.ETag;
                        lastProblem = null;

                        if (!changed.Snapshot.IsWritable && !paused)
                        {
                            Log.Warn($"Not writable now ({changed.Snapshot.NotWritableReason}). Waiting.");
                        }

                        paused = !changed.Snapshot.IsWritable;
                        break;

                    case LiveRead.Refused refused when etag is null && lastProblem is null:
                        Log.Error(refused.Message);

                        return ExitCodes.Failed;

                    case LiveRead.Refused refused when refused.Message != lastProblem:
                        Log.Error(refused.Message);
                        lastProblem = refused.Message;
                        break;

                    case LiveRead.Unreachable unreachable when unreachable.Message != lastProblem:
                        Log.Warn($"Cannot reach the site ({unreachable.Message}). Still trying.");
                        lastProblem = unreachable.Message;
                        break;
                }

                await Task.Delay(mSettings.Interval, stopping);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            Log.Plain("Stopped before any input was waiting.");

            return ExitCodes.Stopped;
        }
    }
}

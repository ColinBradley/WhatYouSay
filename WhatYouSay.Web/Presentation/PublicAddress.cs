namespace WhatYouSay.Web.Presentation;

/// <summary>
/// The address that phones in the room use to reach the site, from the <c>PublicBaseUrl</c>
/// setting. Links handed to an audience, such as the presentation page's QR code, are built on it.
/// </summary>
public sealed record PublicAddress
{
    public const string SettingName = "PublicBaseUrl";

    /// <summary>The configured address, ending in a slash. Null when the setting is empty or missing.</summary>
    public required Uri? BaseUrl { get; init; }

    public bool IsConfigured =>
        this.BaseUrl is not null;

    /// <summary>
    /// Reads the setting. Throws <see cref="InvalidOperationException"/> when the setting holds
    /// anything but an absolute http or https address without a query or fragment.
    /// A typo therefore stops the site starting, rather than putting a QR code on screen that
    /// leads nowhere.
    /// </summary>
    public static PublicAddress From(IConfiguration configuration)
    {
        var value = configuration[SettingName];

        if (string.IsNullOrWhiteSpace(value))
        {
            return new PublicAddress()
            {
                BaseUrl = null,
            };
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https")
            || url.Query.Length > 0
            || url.Fragment.Length > 0)
        {
            throw new InvalidOperationException(
                $"{SettingName} must be an absolute http or https address with no query, such as https://feedback.example.com. It is \"{value}\".");
        }

        return new PublicAddress()
        {
            BaseUrl = url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/"),
        };
    }

    /// <summary>
    /// Returns the absolute URL of <paramref name="path"/>, which has no leading slash.
    /// The URL is built on the public address, or on <paramref name="requestBase"/> when none is
    /// configured. <paramref name="requestBase"/> is the address the current request used.
    /// </summary>
    public Uri Resolve(string path, string requestBase)
    {
        return new Uri(this.BaseUrl ?? new Uri(requestBase), path);
    }
}

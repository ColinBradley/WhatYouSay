using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Net.Codecrete.QrCodeGenerator;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>How to join a topic from a phone: a QR code for the respond page, its URL and the topic code.</summary>
public partial class JoinPanel
{
    /// <summary>
    /// The light margin round the code, in modules. Four is the quiet zone that the QR standard
    /// asks for.
    /// </summary>
    private const int QuietZone = 4;

    private string? mEncodedUrl;

    private string mPath = string.Empty;

    private int mSide;

    /// <summary>The URL split after each path slash, so a narrow column wraps it between segments.</summary>
    private IReadOnlyList<string> mUrlParts = [];

    /// <summary>The respond page's absolute URL.</summary>
    [Parameter]
    [EditorRequired]
    public string Url { get; set; } = string.Empty;

    [Parameter]
    [EditorRequired]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    public bool IsAcceptingResponses { get; set; } = true;

    /// <summary>
    /// Whether to warn that no public address is configured, so <see cref="Url"/> is built on the
    /// address the browser used. Only an admin is shown the warning.
    /// </summary>
    [Parameter]
    public bool ShowsFallbackWarning { get; set; }

    protected override void OnParametersSet()
    {
        // Encoding is cheap, but every live reload sets parameters again.
        if (this.Url == mEncodedUrl)
        {
            return;
        }

        var code = QrCode.EncodeText(this.Url, QrCode.Ecc.Medium);

        mPath = code.ToGraphicsPath(QuietZone);
        mSide = code.Size + (2 * QuietZone);
        mUrlParts = AfterPathSlash().Split(this.Url);
        mEncodedUrl = this.Url;
    }

    /// <summary>The point after each slash that no other slash follows, so the scheme's double slash stays whole.</summary>
    [GeneratedRegex("(?<=/)(?=[^/])")]
    private static partial Regex AfterPathSlash();
}

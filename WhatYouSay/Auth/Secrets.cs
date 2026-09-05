using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace WhatYouSay.Auth;

/// <summary>
/// Three secrets, three treatments, for three different reasons. Admin passwords are
/// human-chosen and therefore reused elsewhere, so they get a slow KDF. Summariser and
/// responder tokens are 256 bits of entropy we generated ourselves, so a slow KDF there
/// would only make every request slow for no security gain.
/// </summary>
public static class Secrets
{
    /// <summary>Unambiguous alphabet: no 0/O, no 1/l/I, so codes survive being read aloud.</summary>
    private const string CodeAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";

    private const int CodeLength = 7;

    private static readonly PasswordHasher<object> sHasher = new();

    private static readonly object sHashSubject = new();

    public static string NewToken() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string NewTopicCode() =>
        RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);

    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string HashPassword(string password) =>
        sHasher.HashPassword(sHashSubject, password);

    public static bool VerifyPassword(string hash, string password) =>
        sHasher.VerifyHashedPassword(sHashSubject, hash, password) != PasswordVerificationResult.Failed;

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

using System.Text.Json;

namespace WhatYouSay.Web.Api;

/// <summary>
/// What a runner hands to a model with each snapshot: the instructions and the shape of the
/// answer. Served from here so the prompt is maintained in one place, and a runner needs no
/// copy of it.
/// </summary>
public static class SummariserModel
{
    /// <summary>
    /// Raise it whenever the instructions, the schema or the snapshot's shape change. It is
    /// part of the snapshot's ETag, so a runner holding an older snapshot reads the new one.
    /// </summary>
    public const int ContractVersion = 1;

    /// <summary>The system prompt for one generation. The snapshot's <c>input</c> is the user message.</summary>
    public const string Instructions =
        $$"""
        You maintain the live summary of a WhatYouSay topic while people are still giving
        feedback. Each time you are called you get the topic's current input as JSON, and
        you return the next version of the summary as JSON. Nobody reviews your answer
        before it is published, and nobody is available to answer questions, so use your
        own judgement and always return a complete summary.

        THE INPUT

          topic       The question people were asked, and whether names and times were
                      recorded.
          guidance    What the organiser expects and wants. Any field may be null.
          responses   Every live response, newest first where times are recorded. Each
                      has an id, a revision number, its current body and reaction counts.
          summary     The live summary as it stands, or null when there is none yet. Each
                      node has an id, the quotes it cites, reaction counts, and comments
                      from readers. A comment lists the commenter's own responses by id.

        THE ANSWER

        One JSON object matching the output schema, and nothing else: "body" holds two or
        three paragraphs of markdown, and "nodes" holds the tree. Every reference names a
        responseId, the revision of that response the quote is copied from, and the quote.
        Use the revision numbers the input gives.

        {{SummariserBriefing.Shape}}

        {{SummariserBriefing.Rules}}

        {{SummariserBriefing.Revising}}

        {{SummariserBriefing.Quality}}
        """;

    /// <summary>A JSON Schema (draft 2020-12) for the answer. Parsed once, and immutable.</summary>
    public static JsonElement OutputSchema { get; } = Parse(
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "required": ["body", "nodes"],
          "properties": {
            "body": { "type": "string" },
            "nodes": { "type": "array", "minItems": 1, "items": { "$ref": "#/$defs/node" } }
          },
          "$defs": {
            "node": {
              "type": "object",
              "additionalProperties": false,
              "anyOf": [{ "required": ["id"] }, { "required": ["text"] }],
              "properties": {
                "id": { "type": "integer", "description": "An existing node to keep. Leave out for a new node." },
                "text": { "type": "string", "minLength": 1, "description": "Leave out to carry an existing node forward unchanged." },
                "references": { "type": "array", "items": { "$ref": "#/$defs/reference" } },
                "children": { "type": "array", "items": { "$ref": "#/$defs/node" } }
              }
            },
            "reference": {
              "type": "object",
              "additionalProperties": false,
              "required": ["responseId", "revision", "quote"],
              "properties": {
                "responseId": { "type": "string", "format": "uuid" },
                "revision": { "type": "integer", "minimum": 1 },
                "quote": { "type": "string", "minLength": 1 }
              }
            }
          }
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}

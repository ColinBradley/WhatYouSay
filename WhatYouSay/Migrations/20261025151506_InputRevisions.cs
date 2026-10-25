using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class InputRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExpectedFeedback",
                table: "Topics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputRevision",
                table: "Topics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SummarisingInstructions",
                table: "Topics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SummaryLengthTarget",
                table: "Topics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AppliedInputRevision",
                table: "Summaries",
                type: "INTEGER",
                nullable: true);

            // Each trigger moves the owning topic's input revision in the same statement as a
            // change the summariser would read. Node feedback only counts on the current tree
            // of the live summary, so archiving a node and feedback on other summaries move
            // nothing. Recursive triggers are off, so the UPDATE of "InputRevision" fires none.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "Responses_MoveInputRevision_Insert" AFTER INSERT ON "Responses"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1 WHERE "Id" = NEW."TopicId";
                END;

                CREATE TRIGGER "Responses_MoveInputRevision_Update"
                AFTER UPDATE OF "IsDeleted", "Author", "RevisionNumber" ON "Responses"
                WHEN OLD."IsDeleted" IS NOT NEW."IsDeleted"
                    OR OLD."Author" IS NOT NEW."Author"
                    OR OLD."RevisionNumber" IS NOT NEW."RevisionNumber"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1 WHERE "Id" = NEW."TopicId";
                END;

                CREATE TRIGGER "Responses_MoveInputRevision_Delete" AFTER DELETE ON "Responses"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1 WHERE "Id" = OLD."TopicId";
                END;

                CREATE TRIGGER "ResponseReactions_MoveInputRevision_Insert" AFTER INSERT ON "ResponseReactions"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1
                    WHERE "Id" = (SELECT "TopicId" FROM "Responses" WHERE "Id" = NEW."ResponseId");
                END;

                CREATE TRIGGER "ResponseReactions_MoveInputRevision_Delete" AFTER DELETE ON "ResponseReactions"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1
                    WHERE "Id" = (SELECT "TopicId" FROM "Responses" WHERE "Id" = OLD."ResponseId");
                END;

                CREATE TRIGGER "NodeReactions_MoveInputRevision_Insert" AFTER INSERT ON "NodeReactions"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1
                    WHERE "Id" = (
                        SELECT s."TopicId" FROM "SummaryNodes" AS n JOIN "Summaries" AS s ON s."Id" = n."SummaryId"
                        WHERE n."Id" = NEW."NodeId" AND s."IsLive" = 1 AND n."ArchivedAt" IS NULL);
                END;

                CREATE TRIGGER "NodeReactions_MoveInputRevision_Delete" AFTER DELETE ON "NodeReactions"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1
                    WHERE "Id" = (
                        SELECT s."TopicId" FROM "SummaryNodes" AS n JOIN "Summaries" AS s ON s."Id" = n."SummaryId"
                        WHERE n."Id" = OLD."NodeId" AND s."IsLive" = 1 AND n."ArchivedAt" IS NULL);
                END;

                CREATE TRIGGER "NodeComments_MoveInputRevision_Insert" AFTER INSERT ON "NodeComments"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1
                    WHERE "Id" = (
                        SELECT s."TopicId" FROM "SummaryNodes" AS n JOIN "Summaries" AS s ON s."Id" = n."SummaryId"
                        WHERE n."Id" = NEW."NodeId" AND s."IsLive" = 1 AND n."ArchivedAt" IS NULL);
                END;

                CREATE TRIGGER "NodeComments_MoveInputRevision_Update"
                AFTER UPDATE OF "Body", "Author", "IsHidden" ON "NodeComments"
                WHEN OLD."Body" IS NOT NEW."Body"
                    OR OLD."Author" IS NOT NEW."Author"
                    OR OLD."IsHidden" IS NOT NEW."IsHidden"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1
                    WHERE "Id" = (
                        SELECT s."TopicId" FROM "SummaryNodes" AS n JOIN "Summaries" AS s ON s."Id" = n."SummaryId"
                        WHERE n."Id" = NEW."NodeId" AND s."IsLive" = 1 AND n."ArchivedAt" IS NULL);
                END;

                CREATE TRIGGER "NodeComments_MoveInputRevision_Delete" AFTER DELETE ON "NodeComments"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1
                    WHERE "Id" = (
                        SELECT s."TopicId" FROM "SummaryNodes" AS n JOIN "Summaries" AS s ON s."Id" = n."SummaryId"
                        WHERE n."Id" = OLD."NodeId" AND s."IsLive" = 1 AND n."ArchivedAt" IS NULL);
                END;

                CREATE TRIGGER "Topics_MoveInputRevision_Update"
                AFTER UPDATE OF "Title", "Description", "ResponseIdentity", "ResponseTiming",
                    "ExpectedFeedback", "SummarisingInstructions", "SummaryLengthTarget" ON "Topics"
                WHEN OLD."Title" IS NOT NEW."Title"
                    OR OLD."Description" IS NOT NEW."Description"
                    OR OLD."ResponseIdentity" IS NOT NEW."ResponseIdentity"
                    OR OLD."ResponseTiming" IS NOT NEW."ResponseTiming"
                    OR OLD."ExpectedFeedback" IS NOT NEW."ExpectedFeedback"
                    OR OLD."SummarisingInstructions" IS NOT NEW."SummarisingInstructions"
                    OR OLD."SummaryLengthTarget" IS NOT NEW."SummaryLengthTarget"
                BEGIN
                    UPDATE "Topics" SET "InputRevision" = "InputRevision" + 1 WHERE "Id" = NEW."Id";
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER "Responses_MoveInputRevision_Insert";
                DROP TRIGGER "Responses_MoveInputRevision_Update";
                DROP TRIGGER "Responses_MoveInputRevision_Delete";
                DROP TRIGGER "ResponseReactions_MoveInputRevision_Insert";
                DROP TRIGGER "ResponseReactions_MoveInputRevision_Delete";
                DROP TRIGGER "NodeReactions_MoveInputRevision_Insert";
                DROP TRIGGER "NodeReactions_MoveInputRevision_Delete";
                DROP TRIGGER "NodeComments_MoveInputRevision_Insert";
                DROP TRIGGER "NodeComments_MoveInputRevision_Update";
                DROP TRIGGER "NodeComments_MoveInputRevision_Delete";
                DROP TRIGGER "Topics_MoveInputRevision_Update";
                """);

            migrationBuilder.DropColumn(
                name: "ExpectedFeedback",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "InputRevision",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "SummarisingInstructions",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "SummaryLengthTarget",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "AppliedInputRevision",
                table: "Summaries");
        }
    }
}

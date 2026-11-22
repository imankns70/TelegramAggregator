using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TelegramAggregator.WebAPI.Data;

namespace TelegramAggregator.WebAPI.Migrations;

[DbContext(typeof(TelegramAggregatorContext))]
[Migration("20261122090000_SeedReferenceData")]
public class SeedReferenceData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO "NewsStatus" ("Id", "Title") VALUES
                (1, 'دریافت'), (2, 'رد'), (3, 'در صف'), (4, 'ارسال شد'), (5, 'منقضی شده')
                ON CONFLICT ("Id") DO NOTHING;
            INSERT INTO "SportSource" ("Id", "Title") VALUES
                (1, 'ورزش سه | آخرین اخبار ورزشی'), (2, 'طرفداری'),
                (3, 'خبرورزشی؛ آخرین اخبار ورزشی ایران و جهان'), (4, 'فوتبال 360')
                ON CONFLICT ("Id") DO NOTHING;
            CREATE INDEX IF NOT EXISTS "IX_News_QueueScorePubDate"
                ON "News" ("NewsStatusId", "Score" DESC, "PubDate" DESC);
            CREATE INDEX IF NOT EXISTS "IX_News_Link" ON "News" ("Link");
            CREATE INDEX IF NOT EXISTS "IX_News_InsertDate" ON "News" ("InsertDate");
            CREATE INDEX IF NOT EXISTS "IX_NewsHistory_InsertDate" ON "NewsHistory" ("InsertDate");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reference data may already be used by news. Preserve it during rollback.
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_News_QueueScorePubDate";
            DROP INDEX IF EXISTS "IX_News_Link";
            DROP INDEX IF EXISTS "IX_News_InsertDate";
            DROP INDEX IF EXISTS "IX_NewsHistory_InsertDate";
            """);
    }
}

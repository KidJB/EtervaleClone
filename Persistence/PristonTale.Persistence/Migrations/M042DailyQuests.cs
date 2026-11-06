using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// A character's daily quests (DailyQuestCodec JSON; empty: none).
[Migration(42)]
public sealed class M042DailyQuests : Migration
{
    public override void Up()
    {
        Alter.Table("characters").AddColumn("daily_quests").AsString().NotNullable().WithDefaultValue("");
    }

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down()
    {
        Execute.Sql("ALTER TABLE characters DROP COLUMN daily_quests");
    }
}

using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// A character's Quest Courier hunts (CourierQuestCodec JSON; empty: none).
[Migration(53)]
public sealed class M053CourierQuests : Migration
{
    public override void Up()
    {
        Alter.Table("characters").AddColumn("courier_quests").AsString().NotNullable().WithDefaultValue("");
    }

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down()
    {
        Execute.Sql("ALTER TABLE characters DROP COLUMN courier_quests");
    }
}

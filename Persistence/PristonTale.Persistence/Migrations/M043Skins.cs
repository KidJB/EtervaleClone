using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Skins: the looks an account owns and the ones a character shows (SkinCodec JSON; empty: none).
[Migration(43)]
public sealed class M043Skins : Migration
{
    public override void Up()
    {
        Create.Table("account_skins")
            .WithColumn("account_id").AsInt64().NotNullable().PrimaryKey().ForeignKey("accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("item_key").AsString(32).NotNullable().PrimaryKey();
        Alter.Table("characters").AddColumn("skins").AsString().NotNullable().WithDefaultValue("");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE characters DROP COLUMN skins");
        Delete.Table("account_skins");
    }
}

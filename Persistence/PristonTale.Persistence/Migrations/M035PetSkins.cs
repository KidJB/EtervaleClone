using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// A character's pets: the skins its account unlocked (a bit per PetSkins id; the account's set is every one of its rows
// joined) and the skin it has out (0 none).
[Migration(35)]
public sealed class M035PetSkins : Migration
{
    public override void Up()
    {
        Alter.Table("characters").AddColumn("pet_skins").AsInt64().NotNullable().WithDefaultValue(0);
        Alter.Table("characters").AddColumn("pet_out").AsInt16().NotNullable().WithDefaultValue(0);
    }

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down()
    {
        Execute.Sql("ALTER TABLE characters DROP COLUMN pet_out");
        Execute.Sql("ALTER TABLE characters DROP COLUMN pet_skins");
    }
}

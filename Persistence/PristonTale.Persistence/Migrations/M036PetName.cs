using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The name a character gave its pet (empty: the pet shows its skin's name).
[Migration(36)]
public sealed class M036PetName : Migration
{
    public override void Up()
    {
        Alter.Table("characters").AddColumn("pet_name").AsString(64).NotNullable().WithDefaultValue("");
    }

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down()
    {
        Execute.Sql("ALTER TABLE characters DROP COLUMN pet_name");
    }
}

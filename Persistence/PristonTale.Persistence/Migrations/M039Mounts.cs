using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The mounts a character's account owns (a bit per Mounts id, every row of the account joined at login) and the one the
// character picked (0 none).
[Migration(39)]
public sealed class M039Mounts : Migration
{
    public override void Up()
    {
        Alter.Table("characters").AddColumn("mounts").AsInt64().NotNullable().WithDefaultValue(0);
        Alter.Table("characters").AddColumn("mount_chosen").AsInt16().NotNullable().WithDefaultValue(0);
    }

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down()
    {
        Execute.Sql("ALTER TABLE characters DROP COLUMN mount_chosen");
        Execute.Sql("ALTER TABLE characters DROP COLUMN mounts");
    }
}

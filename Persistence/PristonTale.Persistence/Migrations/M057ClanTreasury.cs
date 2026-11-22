using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The clan treasury (gold its members donated, which the chief spends) and the clan warehouse's upgrade levels
// (ClanUpgrades: 0 the base warehouse, at most 5).
[Migration(57)]
public sealed class M057ClanTreasury : Migration
{
    public override void Up()
    {
        Alter.Table("clans").AddColumn("treasury").AsInt64().NotNullable().WithDefaultValue(0);
        Alter.Table("clans").AddColumn("warehouse_slot_level").AsInt16().NotNullable().WithDefaultValue(0);
        Alter.Table("clans").AddColumn("warehouse_weight_level").AsInt16().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE clans DROP COLUMN warehouse_weight_level");
        Execute.Sql("ALTER TABLE clans DROP COLUMN warehouse_slot_level");
        Execute.Sql("ALTER TABLE clans DROP COLUMN treasury");
    }
}

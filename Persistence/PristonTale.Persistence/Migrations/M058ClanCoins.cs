using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The clan coin treasury (coin-shop coins its members donated, spent on clan world buffs) and the vice chief's new right to
// spend it (ClanPermission.SpendCoins, bit 16 of the vice's byte: 0x1000), on by default for the clans already stored.
[Migration(58)]
public sealed class M058ClanCoins : Migration
{
    public override void Up()
    {
        Alter.Table("clans").AddColumn("coin_treasury").AsInt64().NotNullable().WithDefaultValue(0);
        Execute.Sql("UPDATE clans SET rank_permissions = rank_permissions | 4096");
    }

    public override void Down()
    {
        Execute.Sql("UPDATE clans SET rank_permissions = rank_permissions & ~4096");
        Execute.Sql("ALTER TABLE clans DROP COLUMN coin_treasury");
    }
}

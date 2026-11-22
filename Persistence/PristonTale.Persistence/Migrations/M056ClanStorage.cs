using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// What a clan owns: its rank rights (ClanRankPermissions packed: the members' in the low byte, the vice's in the next;
// 0x0F03 is the default), its running world buffs (kind, end in Unix seconds), its warehouse (SavedItems JSON whose
// anchors span its pages, with its own revision) and the warehouse's log. Dissolving the clan removes them all.
[Migration(56)]
public sealed class M056ClanStorage : Migration
{
    public override void Up()
    {
        Alter.Table("clans").AddColumn("rank_permissions").AsInt32().NotNullable().WithDefaultValue(0x0F03);

        Create.Table("clan_buffs")
            .WithColumn("clan_id").AsInt64().PrimaryKey().ForeignKey("clan_buffs_clan_fk", "clans", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("kind").AsInt16().PrimaryKey()
            .WithColumn("ends_at").AsInt64().NotNullable();

        Create.Table("clan_warehouses")
            .WithColumn("clan_id").AsInt64().PrimaryKey().ForeignKey("clan_warehouses_clan_fk", "clans", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("revision").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("items").AsString().NotNullable().WithDefaultValue("[]");
        IfDatabase("PostgreSQL").Execute.Sql(
            "ALTER TABLE clan_warehouses ALTER COLUMN items TYPE jsonb USING items::jsonb, ALTER COLUMN items SET DEFAULT '[]'::jsonb;");

        Create.Table("clan_warehouse_log")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("clan_id").AsInt64().NotNullable().ForeignKey("clan_warehouse_log_clan_fk", "clans", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("at").AsInt64().NotNullable()
            .WithColumn("character_name").AsString(64).NotNullable()
            .WithColumn("action").AsInt16().NotNullable()
            .WithColumn("item_key").AsString(16).NotNullable()
            .WithColumn("item_name").AsString(128).NotNullable()
            .WithColumn("quantity").AsInt32().NotNullable();
        Create.Index("ix_clan_warehouse_log_clan").OnTable("clan_warehouse_log").OnColumn("clan_id").Ascending().OnColumn("id").Descending();
    }

    public override void Down()
    {
        Delete.Table("clan_warehouse_log");
        Delete.Table("clan_warehouses");
        Delete.Table("clan_buffs");
        Execute.Sql("ALTER TABLE clans DROP COLUMN rank_permissions");
    }
}

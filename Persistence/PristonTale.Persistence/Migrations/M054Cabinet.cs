using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The Cabinet's collection and bidding deals. An account's unlocked looks keep when, how (CosmeticSource) and by which
// character they were unlocked; every unlock is bound to the account. A deal keeps its leading bid on its row so a bid is one
// compare-and-set update (two bids racing cannot both lead), and every bid placed is logged.
[Migration(54)]
public sealed class M054Cabinet : Migration
{
    public override void Up()
    {
        Alter.Table("account_skins")
            .AddColumn("unlocked_at").AsInt64().NotNullable().WithDefaultValue(0)
            .AddColumn("source").AsInt16().NotNullable().WithDefaultValue(1)
            .AddColumn("character_id").AsInt64().NotNullable().WithDefaultValue(0)
            .AddColumn("bound").AsInt16().NotNullable().WithDefaultValue(1);
        Create.Table("cabinet_deals")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("item_key").AsString(32).NotNullable()
            .WithColumn("starts_at").AsInt64().NotNullable()
            .WithColumn("ends_at").AsInt64().NotNullable()
            .WithColumn("start_bid").AsInt64().NotNullable()
            .WithColumn("increment").AsInt64().NotNullable()
            .WithColumn("min_level").AsInt32().NotNullable()
            .WithColumn("top_bid").AsInt64().NotNullable()
            .WithColumn("top_account_id").AsInt64().NotNullable()
            .WithColumn("top_name").AsString(32).NotNullable()
            .WithColumn("bids").AsInt32().NotNullable()
            .WithColumn("state").AsInt16().NotNullable()
            .WithColumn("created_by").AsString(32).NotNullable()
            .WithColumn("closed_at").AsInt64().NotNullable();
        Create.Index("ix_cabinet_deals_state").OnTable("cabinet_deals").OnColumn("state").Ascending().OnColumn("ends_at").Ascending();
        Create.Table("cabinet_bids")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("deal_id").AsInt64().NotNullable().ForeignKey("fk_cabinet_bids_deal", "cabinet_deals", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("account_id").AsInt64().NotNullable().ForeignKey("fk_cabinet_bids_account", "accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("name").AsString(32).NotNullable()
            .WithColumn("amount").AsInt64().NotNullable()
            .WithColumn("placed_at").AsInt64().NotNullable();
        Create.Index("ix_cabinet_bids_deal").OnTable("cabinet_bids").OnColumn("deal_id").Ascending().OnColumn("amount").Descending();
    }

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down()
    {
        Delete.Table("cabinet_bids");
        Delete.Table("cabinet_deals");
        Execute.Sql("ALTER TABLE account_skins DROP COLUMN bound");
        Execute.Sql("ALTER TABLE account_skins DROP COLUMN character_id");
        Execute.Sql("ALTER TABLE account_skins DROP COLUMN source");
        Execute.Sql("ALTER TABLE account_skins DROP COLUMN unlocked_at");
    }
}

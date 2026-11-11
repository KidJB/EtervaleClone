using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The Character Market: listings keep what they show, a sale row per sold listing (operation_id refuses a repeated purchase),
// and an audit row per change. A partial unique index keeps one active listing per character on both providers.
[Migration(51)]
public sealed class M051CharacterMarket : Migration
{
    public override void Up()
    {
        Create.Table("character_market_listings")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("operation_id").AsString(36).NotNullable().Unique("ux_character_market_listing_operation")
            .WithColumn("character_id").AsInt64().Nullable().ForeignKey("fk_character_market_character", "characters", "id").OnDelete(System.Data.Rule.SetNull)
            .WithColumn("seller_account_id").AsInt64().NotNullable().ForeignKey("fk_character_market_seller", "accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("seller_name").AsString(32).NotNullable()
            .WithColumn("seller_hidden").AsInt16().NotNullable()
            .WithColumn("name").AsString(32).NotNullable()
            .WithColumn("name_key").AsString(32).NotNullable()
            .WithColumn("job_code").AsInt32().NotNullable()
            .WithColumn("level").AsInt32().NotNullable()
            .WithColumn("tier").AsInt32().NotNullable()
            .WithColumn("exp_basis_points").AsInt32().NotNullable()
            .WithColumn("face_index").AsInt32().NotNullable()
            .WithColumn("armor_key").AsString(32).NotNullable()
            .WithColumn("weapon_key").AsString(32).NotNullable()
            .WithColumn("off_hand_key").AsString(32).NotNullable()
            .WithColumn("price").AsInt64().NotNullable()
            .WithColumn("fee").AsInt64().NotNullable()
            .WithColumn("state").AsInt16().NotNullable()
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("expires_at").AsInt64().NotNullable()
            .WithColumn("closed_at").AsInt64().Nullable();
        Create.Index("ix_character_market_browse").OnTable("character_market_listings").OnColumn("state").Ascending().OnColumn("created_at").Descending();
        Create.Index("ix_character_market_price").OnTable("character_market_listings").OnColumn("state").Ascending().OnColumn("price").Ascending();
        Create.Index("ix_character_market_job").OnTable("character_market_listings").OnColumn("state").Ascending().OnColumn("job_code").Ascending();
        Create.Index("ix_character_market_seller").OnTable("character_market_listings").OnColumn("seller_account_id").Ascending().OnColumn("state").Ascending();
        Execute.Sql("CREATE UNIQUE INDEX ux_character_market_active ON character_market_listings (character_id) WHERE state = 0");

        Create.Table("character_market_sales")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("operation_id").AsString(36).NotNullable().Unique("ux_character_market_sale_operation")
            .WithColumn("listing_id").AsInt64().NotNullable().Unique("ux_character_market_sale_listing")
                .ForeignKey("fk_character_market_sale_listing", "character_market_listings", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("character_id").AsInt64().Nullable()
            .WithColumn("character_name").AsString(32).NotNullable()
            .WithColumn("seller_account_id").AsInt64().NotNullable()
            .WithColumn("buyer_account_id").AsInt64().NotNullable().ForeignKey("fk_character_market_sale_buyer", "accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("price").AsInt64().NotNullable()
            .WithColumn("tax").AsInt64().NotNullable()
            .WithColumn("seller_received").AsInt64().NotNullable()
            .WithColumn("created_at").AsInt64().NotNullable();
        Create.Index("ix_character_market_sale_time").OnTable("character_market_sales").OnColumn("created_at").Descending();

        Create.Table("character_market_audit")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("listing_id").AsInt64().NotNullable()
            .WithColumn("action").AsString(16).NotNullable()
            .WithColumn("actor_account_id").AsInt64().Nullable()
            .WithColumn("detail").AsString(256).NotNullable()
            .WithColumn("created_at").AsInt64().NotNullable();
        Create.Index("ix_character_market_audit_listing").OnTable("character_market_audit").OnColumn("listing_id");
    }

    public override void Down()
    {
        Delete.Table("character_market_audit");
        Delete.Table("character_market_sales");
        Delete.Table("character_market_listings");
    }
}

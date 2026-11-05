using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The Item Distributor: one row per item waiting for an account, removed when a character takes or discards it.
[Migration(40)]
public sealed class M040ItemBox : Migration
{
    public override void Up()
    {
        Create.Table("item_box")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("account_id").AsInt64().NotNullable().ForeignKey("accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("sender").AsString(64).NotNullable()
            .WithColumn("item").AsString().NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("ix_item_box_account").OnTable("item_box").OnColumn("account_id");
    }

    public override void Down()
    {
        Delete.Table("item_box");
    }
}

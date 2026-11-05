using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The coin shop's currency: each account's coins and the log of every change to them (grants, purchases, trades).
[Migration(38)]
public sealed class M038Coins : Migration
{
    public override void Up()
    {
        Alter.Table("accounts").AddColumn("coins").AsInt64().NotNullable().WithDefaultValue(0);
        Create.Table("coin_log")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("account_id").AsInt64().NotNullable().ForeignKey("accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("change").AsInt64().NotNullable()
            .WithColumn("balance").AsInt64().NotNullable()
            .WithColumn("reason").AsString().NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("ix_coin_log_account").OnTable("coin_log").OnColumn("account_id");
    }

    public override void Down()
    {
        Delete.Table("coin_log");
        Execute.Sql("ALTER TABLE accounts DROP COLUMN coins");
    }
}

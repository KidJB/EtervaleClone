using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Offline stalls: one per account, where it stands, how it looks (its owner) and its stock, kept until its owner closes it.
[Migration(41)]
public sealed class M041Stalls : Migration
{
    public override void Up()
    {
        Create.Table("stalls")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("account_id").AsInt64().NotNullable().Unique().ForeignKey("accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("character_id").AsInt64().NotNullable().ForeignKey("characters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("field_id").AsInt32().NotNullable()
            .WithColumn("x").AsFloat().NotNullable()
            .WithColumn("y").AsFloat().NotNullable()
            .WithColumn("z").AsFloat().NotNullable()
            .WithColumn("facing").AsFloat().NotNullable()
            .WithColumn("look").AsString().NotNullable()
            .WithColumn("items").AsString().NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
    }

    public override void Down()
    {
        Delete.Table("stalls");
    }
}

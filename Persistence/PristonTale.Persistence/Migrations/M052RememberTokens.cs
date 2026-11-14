using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// "Remember me" logins: the SHA-256 of each token a client keeps (never the token itself), its account and its expiry.
[Migration(52)]
public sealed class M052RememberTokens : Migration
{
    public override void Up()
    {
        Create.Table("remember_tokens")
            .WithColumn("token_hash").AsString(64).PrimaryKey()
            .WithColumn("account_id").AsInt64().NotNullable().ForeignKey("fk_remember_tokens_account", "accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("expires_at").AsInt64().NotNullable();
        Create.Index("ix_remember_tokens_account").OnTable("remember_tokens").OnColumn("account_id").Ascending().OnColumn("created_at").Ascending();
    }

    public override void Down()
    {
        Delete.Table("remember_tokens");
    }
}

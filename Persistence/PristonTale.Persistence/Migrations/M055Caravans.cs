using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Caravans: the caravan a character has out (CaravanCodec JSON; empty: none) and each character's caravan storage, a
// warehouse grid with its own revision.
[Migration(55)]
public sealed class M055Caravans : Migration
{
    public override void Up()
    {
        Alter.Table("characters").AddColumn("caravan").AsString().NotNullable().WithDefaultValue("");
        Create.Table("caravan_storage")
            .WithColumn("character_id").AsInt64().PrimaryKey().ForeignKey("characters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("revision").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("items").AsString().NotNullable().WithDefaultValue("[]");
        IfDatabase("PostgreSQL").Execute.Sql(
            "ALTER TABLE caravan_storage ALTER COLUMN items TYPE jsonb USING items::jsonb, ALTER COLUMN items SET DEFAULT '[]'::jsonb;");
    }

    public override void Down()
    {
        Delete.Table("caravan_storage");
        Execute.Sql("ALTER TABLE characters DROP COLUMN caravan");
    }
}

using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Earned badges. holder_key makes a badge's holder unique: the class for a top level badge, one holder per class, and the
// character for a granted one.
[Migration(49)]
public sealed class M049Achievements : Migration
{
    public override void Up()
    {
        Create.Table("character_achievements")
            .WithColumn("achievement_id").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("holder_key").AsInt64().NotNullable().PrimaryKey()
            .WithColumn("character_id").AsInt64().NotNullable().Indexed("ix_character_achievements_character")
            .WithColumn("earned_at").AsInt64().NotNullable();
    }

    public override void Down() => Delete.Table("character_achievements");
}

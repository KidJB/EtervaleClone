using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The title a character chose to show: the badge's id, 0 to show none; no row shows its best badge.
[Migration(50)]
public sealed class M050AchievementTitle : Migration
{
    public override void Up()
    {
        Create.Table("character_titles")
            .WithColumn("character_id").AsInt64().PrimaryKey()
            .WithColumn("achievement_id").AsInt32().NotNullable();
    }

    public override void Down() => Delete.Table("character_titles");
}

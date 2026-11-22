using PristonTale.Persistence.Abstractions.Admin;

namespace PristonTale.AdminPanel;

// The panel's front page: how much of each kind of data there is, and what makes the running server read each change.
public static class AdminOverview
{
    private static readonly (string Table, string Label)[] Counted =
    [
        ("accounts", "Accounts"), ("characters", "Characters"), ("clans", "Clans"), ("items", "Items"), ("item_definitions", "Item definitions"),
        ("monsters", "Monsters"), ("npcs", "NPCs"), ("npc_shop_items", "Shop entries"), ("monster_drop_line_items", "Drop entries"),
        ("fields", "Fields"), ("field_monster_spawns", "Field spawns"), ("server_settings", "Server settings"), ("stalls", "Personal shops"),
        ("character_market_listings", "Character market listings"), ("gm_audit", "Game-master audit lines")
    ];

    public static readonly (string What, string How)[] Reloads =
    [
        ("Items, monsters, drops, NPCs, shops, fields, spawns, motions", "restart the server"),
        ("server_settings", "/reload_settings in game"),
        ("coinshop.json", "/@coinshop_reload in game"),
        ("cabinet.json", "/@cabinet_reload in game"),
        ("couriers.json", "/@courier_reload in game"),
        ("worldbuffs.json", "stop the server first; it rewrites the file while it runs"),
        ("Accounts' game-master level", "the account's next login"),
        ("Characters, accounts, clans of players online", "edit them while the player is offline: the server's next save overwrites them")
    ];

    public static async Task<IReadOnlyList<(string Table, string Label, long Rows)>> CountsAsync(IAdminDataRepository data, CancellationToken ct)
    {
        var tables = (await data.TablesAsync(ct)).Select(static table => table.Name).ToHashSet(StringComparer.Ordinal);
        var counts = new List<(string, string, long)>(Counted.Length);
        foreach (var (table, label) in Counted)
        {
            if (tables.Contains(table) && await data.RowsAsync(table, new AdminQuery(Size: 1), ct) is { } page)
            {
                counts.Add((table, label, page.Total));
            }
        }

        return counts;
    }
}

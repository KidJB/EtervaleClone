namespace PristonTale.Simulation.Items;

// A helper pet beside its owner fetches gold and potions from the ground within Reach of the owner (the hand reaches
// FieldGroundLoot.PickupReach). The server decides from the item's kind; the client, which sees only a drop's catalog
// key, guesses a potion from the life, mana and stamina families' keys and lets the server refuse a wrong guess.
public static class PetFetch
{
    public const float Reach = 200f;

    public static bool LooksLikePotion(string catalogKey) =>
        catalogKey is { Length: > 2 }
        && (catalogKey.StartsWith("PL", StringComparison.OrdinalIgnoreCase) || catalogKey.StartsWith("PM", StringComparison.OrdinalIgnoreCase)
            || catalogKey.StartsWith("PS", StringComparison.OrdinalIgnoreCase));
}

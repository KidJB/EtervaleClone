using System.Text.Json;
using PristonTale.Simulation.Characters;

namespace PristonTale.Persistence.Abstractions.Characters;

// The looks a character shows as stored (characters.skins); empty when it shows none.
public static class SkinCodec
{
    public static string Capture(SkinSet skins) => skins == SkinSet.None ? "" : JsonSerializer.Serialize(skins, CharacterStateCodec.Options);

    public static SkinSet Restore(string json) =>
        string.IsNullOrEmpty(json) ? SkinSet.None : JsonSerializer.Deserialize<SkinSet>(json, CharacterStateCodec.Options) ?? SkinSet.None;
}

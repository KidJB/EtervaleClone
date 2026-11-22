using System.Text.Json;
using PristonTale.Simulation.Characters;

namespace PristonTale.Persistence.Abstractions.Characters;

// The caravan a character has out as stored (characters.caravan); empty when none.
public static class CaravanCodec
{
    public static string Capture(CaravanState? caravan) => caravan is null ? "" : JsonSerializer.Serialize(caravan, CharacterStateCodec.Options);

    public static CaravanState? Restore(string json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<CaravanState>(json, CharacterStateCodec.Options);
}

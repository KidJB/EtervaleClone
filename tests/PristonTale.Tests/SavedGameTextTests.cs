using System.Text.Json;
using PristonTale.Contracts.Texts;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Tests;

internal static class SavedGameTextTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("saved game texts: a line saved as plain text still loads, and a game text round-trips", ReadAsync);
    }

    private static Task ReadAsync()
    {
        Assert.True(JsonSerializer.Deserialize<GameText>("\"Red Dragon Mount joined your stable\"", CharacterJson.Options).Id == GameTextId.PlainText, "an old line");
        Assert.True(JsonSerializer.Deserialize<GameText>("\"\"", CharacterJson.Options).IsNone, "an old empty line");
        var text = GameText.Of(GameTextId.TradeBusy);
        Assert.True(JsonSerializer.Deserialize<GameText>(JsonSerializer.Serialize(text, CharacterJson.Options), CharacterJson.Options) == text, "a game text");
        Assert.True(JsonSerializer.Deserialize<GameText>("{\"Id\":\"None\",\"Args\":[],\"IsNone\":true}", CharacterJson.Options).IsNone, "a saved none");
        return Task.CompletedTask;
    }
}

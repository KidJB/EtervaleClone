using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Contracts.Texts;

namespace PristonTale.Persistence.Abstractions.Characters;

// Saved operation results hold a game text as {Id, Args}; characters saved before game texts hold the line as plain text.
public sealed class GameTextJsonConverter : JsonConverter<GameText>
{
    public override GameText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var line = reader.GetString();
            return string.IsNullOrEmpty(line) ? GameText.None : GameText.Plain(line);
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var id = root.TryGetProperty(nameof(GameText.Id), out var saved) ? Enum.Parse<GameTextId>(saved.ToString()) : GameTextId.None;
        var args = ImmutableArray.CreateBuilder<string>();
        if (root.TryGetProperty(nameof(GameText.Args), out var list))
        {
            foreach (var arg in list.EnumerateArray())
            {
                args.Add(arg.GetString() ?? "");
            }
        }

        return new GameText(id, args.ToImmutable());
    }

    public override void Write(Utf8JsonWriter writer, GameText value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(nameof(GameText.Id), value.Id.ToString());
        writer.WriteStartArray(nameof(GameText.Args));
        foreach (var arg in value.Args)
        {
            writer.WriteStringValue(arg);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

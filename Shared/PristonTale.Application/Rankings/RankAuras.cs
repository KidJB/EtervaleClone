using PristonTale.Contracts.Characters;

namespace PristonTale.Application.Rankings;

// Who wears a ranking aura: the first of the level board of all classes wears Overall, and the first three of each class's
// board wear First, Second and Third. Characters by name (names are unique, compared without case).
public static class RankAuras
{
    public const int ClassPlaces = 3;

    public static Dictionary<string, RankAura> Of(IReadOnlyList<RankedCharacter> overall, IEnumerable<IReadOnlyList<RankedCharacter>> classes)
    {
        var auras = new Dictionary<string, RankAura>(StringComparer.OrdinalIgnoreCase);
        foreach (var board in classes)
        {
            for (var place = 0; place < Math.Min(ClassPlaces, board.Count); place++)
            {
                auras[board[place].Name] = (RankAura)(place + 1);
            }
        }

        if (overall.Count > 0)
        {
            auras[overall[0].Name] = RankAura.Overall;
        }

        return auras;
    }
}

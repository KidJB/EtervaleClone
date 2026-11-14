using PristonTale.Contracts.Characters;
using PristonTale.Protocol.Packets;

namespace PristonTale.Server.Net;

internal sealed partial class ClientSession
{
    // The rankings window's board: the level board of all classes or of one, or Bellatra's best clans.
    private async Task RankingsAsync(RankingsRequestPacket request)
    {
        if (_session is not { Loaded: not null } session)
        {
            return;
        }

        RankingWireEntry[] entries;
        bool available;
        byte job = 0;
        if (request.Board == RankingBoard.Bellatra)
        {
            var clans = await _services.Rankings.BellatraAsync(session.EntityId);
            available = clans is not null;
            entries = clans is null ? [] : [.. clans.Select(static clan => new RankingWireEntry(clan.Name, 0, 0, 0, "", clan.Points))];
        }
        else
        {
            PlayerClass? chosen = request.Class != 0 && Enum.IsDefined((PlayerClass)request.Class) ? (PlayerClass)request.Class : null;
            job = chosen is null ? (byte)0 : request.Class;
            var board = await _services.Rankings.LevelsAsync(chosen);
            available = board is not null;
            entries = board is null ? [] : [.. board.Select(static row => new RankingWireEntry(row.Name, (byte)row.Class, row.Level, (ushort)row.ExpBasisPoints, row.Clan, 0))];
        }

        // The board in parts a frame holds; an empty or refused board is one empty part.
        var first = 0;
        do
        {
            var part = entries.AsSpan(first, Math.Min(RankingWire.MaxEntries, entries.Length - first)).ToArray();
            SendLarge(new RankingsPacket(request.Board, job, available, part, first).Write, RankingsPacket.MaxSize);
            first += part.Length;
        }
        while (first < entries.Length);
    }
}

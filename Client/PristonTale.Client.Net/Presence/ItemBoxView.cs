using PristonTale.Simulation.Characters;

namespace PristonTale.Client.Net.Presence;

// A waiting item of the Item Distributor: its row, who sent it and when (UTC), and the item with its values for the player.
public sealed record ItemBoxEntryInfo(long Id, string Sender, DateTime SentAt, OwnedItemView Item);

// An open's answer: its status (the server's ItemBoxStatus) and the waiting items, oldest first.
public sealed record ItemBoxListInfo(byte Status, IReadOnlyList<ItemBoxEntryInfo> Entries);

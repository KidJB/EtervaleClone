using PristonTale.Simulation.Shops;

namespace PristonTale.Client.Net.Presence;

// The coin shop as an open request answered it: its articles and the account's coins at that moment.
public sealed record CoinShopView(CoinShopCatalog Catalog, long Balance);

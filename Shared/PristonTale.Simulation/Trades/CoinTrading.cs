namespace PristonTale.Simulation.Trades;

// Coins a trade moves between accounts: at most MaxPerTrade a side, and the receiver gets the amount less FeePercent
// (rounded up, so no amount crosses free); the fee leaves the game.
public static class CoinTrading
{
    public const long MaxPerTrade = 100_000;
    public const int FeePercent = 10;

    public static long Fee(long coins) => coins <= 0 ? 0 : (coins * FeePercent + 99) / 100;

    public static long Received(long coins) => coins - Fee(coins);

    public static TradeStatus Check(long coins, long balance) =>
        coins < 0 ? TradeStatus.Rejected
        : coins > MaxPerTrade ? TradeStatus.CoinLimit
        : coins > balance ? TradeStatus.NotEnoughCoins
        : TradeStatus.Ok;
}

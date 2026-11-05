# Coin shop

Account coins, the item shop that spends them and the trades that move them: the catalog file, prices, storage,
messages and the client windows.

## Read this when

- Changing what the shop sells, its prices or discounts.
- Changing how coins are granted, spent, traded or logged.
- Changing the coin shop window.

## Code map

| Path | Responsibility |
|---|---|
| `Shared/PristonTale.Simulation/Trades/CoinTrading.cs` | Coins in a trade: the per-trade cap, the fee and the offer check |
| `Shared/PristonTale.Simulation/Shops/CoinShopCatalog.cs` | `CoinShopCatalog`, `CoinShopTab`, `CoinShopItem`, `CoinShopStatus`; `CoinShopPricing` (unit price, quantity, bulk discount, total) |
| `Shared/PristonTale.Application/Services/CoinShopAppService.cs` | `CoinShopCatalogStore` (reads `coinshop.json`), `CoinShopAppService` (balance, purchase) |
| `Shared/PristonTale.Application/GameMasters/GmCoinCommands.cs` | `/@coins` and `/@coinshop_reload` |
| `Persistence/PristonTale.Persistence.Abstractions/Coins/ICoinRepository.cs`, `Persistence/PristonTale.Persistence/Repositories/DapperCoinRepository.cs` | Balance, logged changes, purchase stored with the character |
| `Persistence/PristonTale.Persistence/Migrations/M038Coins.cs` | `accounts.coins` and the `coin_log` table |
| `Shared/PristonTale.Protocol/Packets/CoinShopPackets.cs`, `Server/PristonTale.Server/Net/ClientSession.CoinShop.cs` | Messages and the server's handlers |
| `Client/PristonTale.Client.Net/GameServerConnection.cs` (`OpenCoinShopAsync`, `BuyCoinShopAsync`) | Client requests; tabs collected until the catalog end |
| `Client/PristonTale.Gameplay/UI/Widgets/HudCoinShop.cs`, `World/GameScene.CoinShop.cs` | The window and its requests |
| `Client/PristonTale.Gameplay/UI/Widgets/HudCoinBalance.cs` | The balance the bag, the trade window and the shop show |

## Coins

Coins belong to the account (`accounts.coins`), not to a character. Every change writes a line to `coin_log` (change,
balance after, reason). A change never takes the balance below zero: the update only applies while
`coins + change >= 0`. Game masters grant or take coins with `/@coins <online name> <amount>` (administrator); a
donation site can later grant through the same repository call (`ICoinRepository.AddAsync`).

## Catalog

The shop is `data/coinshop.json` (setting `CoinShop:Path`), read at start and by `/@coinshop_reload`; a missing or
broken file leaves an empty shop and the reload says why. Comments and trailing commas are allowed.

- `Name`, `Message`: the shop's title and the line across its top.
- `Tabs`: `Id`, `Name`, `Icon` (an image in `assets/Hud/CoinShop/categories/`, without `.png`; the tab's name shows
  without it) and `Items`.
- An item: `Id`, `Name`, `Description`, `ItemKey` (the catalog key delivered), `Price`; optionally `Discount` (a price
  that replaces `Price`), `Bulk` and `MaxBulk` (percent off per five bought, capped), `Spec` (the buyer picks the class
  specialization) and `Quantity` (the buyer picks how many, up to 100 and the item's stack).

The wire sends at most 16 tabs of 32 articles each.

## Purchase

`CoinShopAppService.BuyAsync` finds the article and its item, takes the quantity (`CoinShopPricing.Quantity`) and the
total (`CoinShopPricing.Total`), and refuses a balance below it. Then, under the character's save gate
(`DurableOperations`), the tick prepares the item received into the bag (`WorldServer.PrepareGiftAsync`); a full bag
answers `NoRoom` and takes nothing. The coins and the character with the item are written in one transaction
(`ICoinRepository.BuyWithCharacterAsync`) before the commit, so a crash never takes coins without the item or gives it
without them. A stackable article arrives as one stack; gear arrives as its best copy (`ItemRoller.CreatePerfect`),
specialized for the chosen class when the article takes a spec and the item allows it.

## Trading coins

A trade side offers coins with its items and gold (`TradeOffer.Coins`): at most `CoinTrading.MaxPerTrade` (100,000), and
no more than its account holds, read before the offer reaches the tick (`TradeAppService.OfferAsync`); a refused offer
answers "Not enough coins" or "Too many coins for one trade". When the exchange is stored, each side's coins leave its
account and the other account receives them less the fee (`CoinTrading.Fee`, 10% rounded up, which leaves the game), in
the same transaction as both characters (`ICoinRepository.TradeWithCharactersAsync`, a log line per change). Coins spent
elsewhere meanwhile refuse the store: nothing moves and the trade opens again unlocked with "Not enough coins". A trade
without coins stores as before (`ICharacterRepository.UpdateAllAsync`).

## Messages

`CoinShopOpen` (C→S) is answered by one `CoinShopTab` per tab, then `CoinShopCatalog` with the balance; `CoinShopBuy`
(C→S) by `CoinShopBuyResult` with the status and the new balance; `CoinBalance` (C→S) by `CoinBalanceResult`. Trades carry
coins in `TradeOffer` and `TradeState`. Layouts are in [protocol.md](protocol.md).

## Client

`K` or the `$` button under the quest book's button opens the window on the item shop frame of `assets/Hud/CoinShop/`
(`background.png`, 560x450): the tabs as their category art down the left panel, the balance by the coin pile, the
shop's message (or the last purchase's result) across the top right, and the open tab's articles as cards three at a
time on the right (the wheel and the scroll bar move them). A card shows the item's icon, name, description and price
(a discounted article also shows its struck-through price), a buy button, a quantity counter for a quantity article and
the class arrows for a spec article (starting at the player's class). Requests are read back without waiting; Escape
closes the window.

The balance (`HudCoinBalance`) is asked of the server when the bag or a trade opens and when a trade ends, and set by a
purchase. The bag shows it right of the gold. The trade window has a strip under its frame: the player's offered coins
with what the partner gets after the fee (a click asks how many, up to the balance and the cap) and the partner's coins
with what the player gets; a change of the partner's coins greys the lock like any other change.

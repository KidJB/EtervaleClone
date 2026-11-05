# Coin shop

Account coins and the item shop that spends them: the catalog file, prices, storage, messages and the client window.

## Read this when

- Changing what the shop sells, its prices or discounts.
- Changing how coins are granted, spent or logged.
- Changing the coin shop window.

## Code map

| Path | Responsibility |
|---|---|
| `Shared/PristonTale.Simulation/Shops/CoinShopCatalog.cs` | `CoinShopCatalog`, `CoinShopTab`, `CoinShopItem`, `CoinShopStatus`; `CoinShopPricing` (unit price, quantity, bulk discount, total) |
| `Shared/PristonTale.Application/Services/CoinShopAppService.cs` | `CoinShopCatalogStore` (reads `coinshop.json`), `CoinShopAppService` (balance, purchase) |
| `Shared/PristonTale.Application/GameMasters/GmCoinCommands.cs` | `/@coins` and `/@coinshop_reload` |
| `Persistence/PristonTale.Persistence.Abstractions/Coins/ICoinRepository.cs`, `Persistence/PristonTale.Persistence/Repositories/DapperCoinRepository.cs` | Balance, logged changes, purchase stored with the character |
| `Persistence/PristonTale.Persistence/Migrations/M038Coins.cs` | `accounts.coins` and the `coin_log` table |
| `Shared/PristonTale.Protocol/Packets/CoinShopPackets.cs`, `Server/PristonTale.Server/Net/ClientSession.CoinShop.cs` | Messages and the server's handlers |
| `Client/PristonTale.Client.Net/GameServerConnection.cs` (`OpenCoinShopAsync`, `BuyCoinShopAsync`) | Client requests; tabs collected until the catalog end |
| `Client/PristonTale.Gameplay/UI/Widgets/HudCoinShop.cs`, `World/GameScene.CoinShop.cs` | The window and its requests |

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

## Messages

`CoinShopOpen` (C→S) is answered by one `CoinShopTab` per tab, then `CoinShopCatalog` with the balance; `CoinShopBuy`
(C→S) by `CoinShopBuyResult` with the status and the new balance. Layouts are in [protocol.md](protocol.md).

## Client

`K` or the `$` button under the quest book's button opens the window on the item shop frame of `assets/Hud/CoinShop/`
(`background.png`, 560x450): the tabs as their category art down the left panel, the balance by the coin pile, the
shop's message (or the last purchase's result) across the top right, and the open tab's articles as cards three at a
time on the right (the wheel and the scroll bar move them). A card shows the item's icon, name, description and price
(a discounted article also shows its struck-through price), a buy button, a quantity counter for a quantity article and
the class arrows for a spec article (starting at the player's class). Requests are read back without waiting; Escape
closes the window.

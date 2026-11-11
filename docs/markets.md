# Markets

The two markets at their NPCs in Ricarten: the Character Market, where characters sell for coins, and the Marketplace, a
search of the personal shops and stalls open right now. Each can be switched off alone.

## Read this when

- Changing listing or purchase rules, tax, fees, expiry or the settings of either market.
- Adding a table tied to a character's owner: the sale must account for it (see Ownership).
- Changing the market windows, their NPCs or their messages.

## Code map

| Path | Responsibility |
|---|---|
| `Shared/PristonTale.Simulation/Markets/CharacterMarket.cs`, `Marketplace.cs` | Statuses, options, tax, categories, filters |
| `Shared/PristonTale.Simulation/Markets/MarketNpcs.cs` | The two NPCs: codes, borrowed models, placements |
| `Shared/PristonTale.Application/Markets/CharacterMarketAppService.cs` | Character Market requests; `MarketOptionsHolder` |
| `Shared/PristonTale.Application/Markets/MarketplaceAppService.cs`, `World/WorldServer.Marketplace.cs` | The live search and purchase |
| `Shared/PristonTale.Application/GameMasters/GmMarketCommands.cs` | `/market_*` commands |
| `Persistence/.../Markets/ICharacterMarketRepository.cs`, `Repositories/DapperCharacterMarketRepository.cs` | Listings, sales, audit |
| `Persistence/PristonTale.Persistence/Migrations/M051CharacterMarket.cs` | The three tables |
| `Shared/PristonTale.Protocol/Packets/MarketPackets.cs`, `Server/PristonTale.Server/Net/ClientSession.Markets.cs` | Messages |
| `Server/PristonTale.Server/World/CharacterMarketExpiryService.cs` | Closes expired listings each minute |
| `Client/PristonTale.Client.Net/Online/MarketRequests.cs` | Client requests |
| `Client/PristonTale.Gameplay/UI/Markets/` | `HudMarketplace`, `HudCharacterMarket`, `HudMarketFrame` |

## Character Market

A listing keeps the character's facts (name, class, level, tier, experience share, worn keys), so browsing reads only
`character_market_listings`, indexed and paged. Details read the character's row for its worn gear and attributes.

Every change needs the player at an NPC offering `NpcServiceKind.CharacterMarket`. Listing and buying hold the
character's claim in `ISessionRegistry`, the claim a world entry takes, so neither runs while the character is in play
or entering. A listed character cannot enter the world (`EnterWorldFailure.ListedForSale`), be deleted or be moved by a
game master, until the listing is cancelled, removed or expires.

`ListAsync` refuses a character not owned by the account, below the minimum level, in a clan, standing as a stall or
already listed, or past the account's limit; the fee is taken in the same transaction and is not returned.

`BuyAsync` is one transaction, opened by a conditional write that marks the listing sold, so of two buyers one wins:
the owner, clan and stall are checked again, the buyer's first free slot is found, coins move in account order (never
below zero, each with a `coin_log` line), the character's `account_id` and `slot` change, and the sale and audit rows
are written. Anything refused or failing rolls all of it back. A repeated `OperationId` answers Ok without a second
sale. The tax is `CharacterMarketRules.Tax`, rounded up, and leaves the game.

### Ownership

Only `characters.account_id` and `slot` change. What belongs to the character travels with it: bag, gear, skills,
quests, premium timers, gold, contacts, achievements. A clan membership or stall blocks the sale, since each ties the
character to more than its owner. What belongs to the account stays with the seller: the warehouse, the Item
Distributor, `account_skins` and the coins. The pet skins and mounts a character holds are saved on it, including what
its account had unlocked when it last played, so they go with it and join the buyer's account at its next entry
(`SessionAppService.EnterWorldAsync`).

## Marketplace

It keeps no stock. `WorldServer.BrowseMarketplaceAsync` reads on the tick every playing seller's shop, after pruning
what left its bag, and every stall, then filters (`MarketplaceRules.Matches`), sorts and answers a page of six. A shop
that closed or a seller who left is simply not there. A purchase is `PersonalShopAppService.BuyAsync` with the
Marketplace NPC's id: the seller may be in any field and the distance is waived only while the buyer stands at that NPC.
Every other check of the personal shop (price and count as shown, gold or coins, bag space, the buyer's revision, the
seller's limit, replay of an operation, the purchase cooldown) applies unchanged. A card shows its price in gold or coins,
or "viewonly." without a purchase button. The window asks for the page again after each purchase.

## Settings

Rows of `server_settings` ([server.md](server.md#server-settings-table)): `market.character.enabled`, `.min_level`,
`.max_listings`, `.min_price`, `.max_price`, `.listing_fee`, `.expiry_days`, `.tax_percent`, and `market.shops.enabled`.
A change applies without a restart.

## Game masters

`/market_listings` and `/market_sales` (Guide) list the latest; `/market_remove <id> [reason]` and `/market_expire`
(Moderator) close listings. Fees, limits and switching off are `/setting market.*` (Administrator).

## Client

`HudMarketFrame` draws the frames, panels, buttons and drop-downs at their size. `assets/Hud/Marketplace/frame.png`
(720x517) and `assets/Hud/CharacterMarket/frame.png` (760x612) replace the drawn frame when present. The cards show the
class's initial where a portrait would stand.
The Marketplace search box searches item names; text starting with `@` searches sellers instead. A card shows the
item, its required level and rarity, the total price, the seller and the field; the item slot shows its tooltip.

## Pitfalls

- A new table holding a character's relationship to others must be added to `ListAsync`'s and `BuyAsync`'s checks.
- `CharacterSlots.Maximum` limits the buyer's account; the slot is the first free one.
- The Marketplace search runs on the tick and walks every open shop: keep its filter cheap.

## Related

[coin-shop.md](coin-shop.md), [application.md](application.md), [protocol.md](protocol.md),
[persistence.md](persistence.md).

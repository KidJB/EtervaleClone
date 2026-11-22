using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Travel;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Localization;
using PristonTale.Contracts.Texts;

namespace PristonTale.Gameplay.UI;

// NPC services: opening their windows, the shop, warehouse and workbench actions, item drops from the bag and the icons they need.
public sealed partial class GameplayHud
{
    private const double TeleportPrefetchDelaySeconds = 0.25;

    private void UpdateTeleportPrefetch(Point pointer)
    {
        var destination = _npcOpen ? _npc.DestinationAt(pointer) : null;
        if (destination is null)
        {
            _hoveredTeleportFieldCode = -1;
            _teleportFieldPrepared = false;
            return;
        }

        if (destination.FieldCode != _hoveredTeleportFieldCode)
        {
            _hoveredTeleportFieldCode = destination.FieldCode;
            _hoveredTeleportSince = _clock.Elapsed.TotalSeconds;
            _teleportFieldPrepared = false;
        }

        if (!_teleportFieldPrepared && _clock.Elapsed.TotalSeconds - _hoveredTeleportSince >= TeleportPrefetchDelaySeconds)
        {
            _prepareField?.Invoke(destination.FieldCode);
            _teleportFieldPrepared = true;
        }
    }

    // A service the server opened shows its window; the NPC dialog gives way to it, as the original opens services directly.
    private void ApplyServices(OwnerState state)
    {
        if (_services.Opened != _serviceOpened)
        {
            _serviceOpened = _services.Opened;
            OpenService();
        }

        if (_services.Open != NpcServiceKind.Shop && _shop.IsOpen)
        {
            _shop.Hide();
        }

        if (_services.Open != NpcServiceKind.ClanWarehouse && _clanWarehouse.IsOpen)
        {
            _clanWarehouse.Hide();
        }

        if (_services.Open is not (NpcServiceKind.Warehouse or NpcServiceKind.Caravan) && _warehouse.IsOpen)
        {
            SendCaravanChanges();
            _warehouse.Hide();
        }

        foreach (var bench in _benches)
        {
            if (_services.Open != bench.Service && bench.IsOpen)
            {
                bench.Hide();
            }

            bench.Sync(state);
        }

        // A recipe placed in the manufacture window asks the server what it makes; taking it back and placing it again
        // asks again (after a refused answer).
        _manufacture.Products = _services.Catalog;
        if (_manufacture.Item == Guid.Empty)
        {
            _manufacture.ProductsFor = Guid.Empty;
        }

        if (_manufacture.FullyOpen && _manufacture.Item != Guid.Empty && _manufacture.ProductsFor != _manufacture.Item
            && _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Manufacture, NpcServiceAction.Refresh, itemId: _manufacture.Item))
        {
            _manufacture.ProductsFor = _manufacture.Item;
        }

        if (Storage.IsOpen && _services.Warehouse is { } view && !ReferenceEquals(view, Storage.View))
        {
            Storage.Refresh(view);
            RequestIcons(_state);
        }

        if (!_services.Pending)
        {
            _services.RefreshQuotes(state);
        }
    }

    // The icon cache keeps only the last requested set, so every request names everything on screen: the pack, the open
    // shop's stock, the open warehouse, a personal shop or stall's offers, the Item Distributor and the coin shop.
    private void RequestIcons(OwnerState state)
    {
        RememberKeys(state?.Items);
        RememberKeys(Storage.View?.Items);
        var keys = state?.Items.Select(static item => item.CatalogKey) ?? [];
        if (_shop.IsOpen)
        {
            keys = keys.Concat(_shop.IconKeys);
        }

        if (Storage.IsOpen && Storage.View is { } view)
        {
            keys = keys.Concat(view.Items.Select(static item => item.CatalogKey));
        }

        if (_personalShop.Window.IsOpen)
        {
            keys = keys.Concat(_personalShop.Window.Offers.Select(static offer => offer.Item.CatalogKey));
        }

        _icons.Request(keys.Concat(_itemBox.IconKeys).Concat(_coinShop.IconKeys).Concat(_cabinet.IconKeys).Concat(_marketplace.IconKeys).Concat(_characterMarket.IconKeys));
    }

    // Keys of the items seen in the pack or the warehouse, so a sold or stored item still has its sound once it is gone.
    private void RememberKeys(IEnumerable<OwnedItemView> items)
    {
        if (_itemKeys.Count > 4096)
        {
            _itemKeys.Clear();
        }

        foreach (var item in items ?? [])
        {
            _itemKeys[item.InstanceId] = item.CatalogKey;
        }
    }

    // A warehouse exchange hands the item it picked up to the cursor, in the bag or on the grid where it now lies.
    private void ServiceSucceeded(NpcServiceCommand command, NpcServiceOutcome outcome)
    {
        ServiceSound(command);
        if (command.Covered == Guid.Empty)
        {
            return;
        }

        if (command.Action == NpcServiceAction.Deposit)
        {
            _inventory.CarryAfter(command.Covered, outcome.Revision);
        }
        else if (command.Action is NpcServiceAction.Move or NpcServiceAction.Withdraw)
        {
            Storage.CarryAfter(command.Covered);
        }
    }

    private void ServiceSound(NpcServiceCommand command)
    {
        var key = command.Action == NpcServiceAction.Buy
            ? ItemCodes.TryFormat(command.ItemCode, out var bought) ? bought : null
            : command.Action is NpcServiceAction.Sell or NpcServiceAction.Deposit or NpcServiceAction.Withdraw or NpcServiceAction.Move
                ? _itemKeys.GetValueOrDefault(command.ItemId)
                : command.Action is NpcServiceAction.DepositGold or NpcServiceAction.WithdrawGold ? ItemCodes.GoldKey : null;
        if (key is not null)
        {
            ItemSound?.Invoke(key);
        }
    }

    // The Item Distributor opens its own window (HudItemBox) and the bag; every other service asks the server to open. True
    // when a window opens or the request went out.
    // The storage the open window shows: the caravan's or the NPC's warehouse.
    // The window showing the open storage: the clan warehouse's own, or the warehouse window (the account's, the caravan's).
    private IHudStorageWindow Storage => _services.Open == NpcServiceKind.ClanWarehouse ? _clanWarehouse : _warehouse;

    private NpcServiceKind StorageKind => _services.Open is NpcServiceKind.Caravan or NpcServiceKind.ClanWarehouse ? _services.Open.Value : NpcServiceKind.Warehouse;

    // Opens the local player's caravan storage (no NPC); the server refuses it while no caravan is out.
    // What the caravan window changed (its name, follow or stay), sent as the chat commands the server takes.
    private void SendCaravanChanges()
    {
        if (!_warehouse.CaravanMode)
        {
            return;
        }

        foreach (var command in _warehouse.CaravanChanges())
        {
            _ = _chat.Send?.Invoke(command, false, Guid.Empty);
        }
    }

    // A click on the own caravan's model in the world opens its storage as its name does.
    public void OpenOwnCaravan()
    {
        if (!ModalOpen)
        {
            OpenCaravan();
        }
    }

    private bool OpenCaravan() => _services.Request(0, ClientText.Get("Caravan.Title"), NpcServiceKind.Caravan, NpcServiceAction.Open);

    private bool OpenNpcService(ulong npcId, string npcName, NpcServiceKind kind)
    {
        if (kind == NpcServiceKind.Cabinet)
        {
            _npcOpen = false;
            _npc.Dismiss();
            _cabinet.Show();
            return true;
        }

        if (kind == NpcServiceKind.CharacterMarket)
        {
            _npcOpen = false;
            _npc.Dismiss();
            _characterMarket.Show(npcId);
            return true;
        }

        if (kind == NpcServiceKind.Marketplace)
        {
            _npcOpen = false;
            _npc.Dismiss();
            _marketplace.Show(npcId);
            return true;
        }

        if (kind == NpcServiceKind.BossArena)
        {
            _npcOpen = false;
            _npc.Dismiss();
            _arenaEvent.Show();
            return true;
        }

        if (kind == NpcServiceKind.Courier)
        {
            _npcOpen = false;
            _npc.Dismiss();
            _courierWindow.Show(npcId);
            return true;
        }

        if (kind == NpcServiceKind.DailyQuest)
        {
            _npcOpen = false;
            _npc.Dismiss();
            _questBook.ShowDaily(npcId);
            return true;
        }

        if (kind != NpcServiceKind.GiftExpress)
        {
            return _services.Request(npcId, npcName, kind, NpcServiceAction.Open);
        }

        _npcOpen = false;
        _npc.Dismiss();
        _itemBox.Show(npcId);
        OpenOnly(_inventorySlide);
        return true;
    }

    // A service window closes the special item box, as the original does for every window it opens.
    private void OpenService()
    {
        _inventory.CloseQuestBox();
        switch (_services.Open)
        {
            case NpcServiceKind.Shop:
                _npcOpen = false;
                _shop.Show(_services.Catalog);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Caravan when _services.Warehouse is { } view:
                _npcOpen = false;
                _warehouse.ShowCaravan(view, PetCollection.CaravanName, !PetCollection.CaravanStays);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.ClanWarehouse when _services.Warehouse is { } view:
                _npcOpen = false;
                _clanWarehouse.Show(view, Clan);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Warehouse when _services.Warehouse is { } view:
                _npcOpen = false;
                _warehouse.Show(view);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Teleporter:
                _npc.ShowDestinations(_services.Destinations);
                break;
            case NpcServiceKind.ItemAging:
                OpenBench(() => _aging.Show(_npc.Current?.Services.FirstOrDefault(static offer => offer.Kind == NpcServiceKind.ItemAging)?.PricePercent ?? 100));
                break;
            case NpcServiceKind.Clan:
                OpenClan(_services.NpcId);
                break;
            case NpcServiceKind.BlessCastle:
                OpenCastle(_services.NpcId);
                break;
            case NpcServiceKind.EventBooth
                when _npc.Current?.Services.FirstOrDefault(static offer => offer.Kind == NpcServiceKind.EventBooth)?.Value == Simulation.World.Bellatra.BellatraRules.SecretaryBooth:
                OpenBellatra(_services.NpcId);
                break;
            case NpcServiceKind.EventBooth:
                OpenBellatraBooth(_npc.Current?.Services.FirstOrDefault(static offer => offer.Kind == NpcServiceKind.EventBooth)?.Value ?? 0);
                break;
            case NpcServiceKind.Smelting:
                OpenBench(_smelting.Show);
                break;
            case NpcServiceKind.Manufacture:
                OpenBench(_manufacture.Show);
                break;
            case NpcServiceKind.MixtureReset:
                OpenBench(_mixtureReset.Show);
                break;
            case NpcServiceKind.ItemMix:
            {
                var offer = _npc.Current?.Services.FirstOrDefault(static offer => offer.Kind == NpcServiceKind.ItemMix);
                OpenBench(() => _mix.Show(offer?.Value == Simulation.Items.Mixing.MixingWork.AlchemyMode, offer?.PricePercent ?? 100));
                break;
            }
            case NpcServiceKind.QuestEvent:
                if (_puzzle.Offer(_state, _services.NpcId, _services.NpcName) is { } help)
                {
                    _notice.Show(help);
                }
                else
                {
                    _npcOpen = false;
                    _npc.Dismiss();
                    _inventorySlide.Open = false;
                }

                break;
            case NpcServiceKind.SkillMaster:
                _npcOpen = false;
                _skillMaster = _services.NpcId;
                OpenOnly(_skillSlide);
                break;
        }
    }

    private void CloseServices()
    {
        SendCaravanChanges();
        _services.Close();
        foreach (var window in _serviceWindows)
        {
            window.Hide();
        }
    }

    private void ClickShop(OwnerState state, Point pointer)
    {
        switch (_shop.Click(pointer))
        {
            case HudShopAction.Exit:
                CloseServices();
                break;
            case HudShopAction.Buy when _shop.Selected is { } offer && state is not null:
                BuyFromShop(offer, state);
                break;
            case HudShopAction.RepairAll when state is not null:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.RepairAll, state.InventoryRevision);
                break;
        }
    }

    // As in the original, goods sold in stacks (potions) ask how many, up to a stack and what the gold pays for; other
    // items buy one at a click.
    private void BuyFromShop(ShopOffer offer, OwnerState state)
    {
        if (offer.MaxStack <= 1)
        {
            BuyFromShop(offer, 1);
            return;
        }

        var affordable = offer.Price > 0 ? state.Money / offer.Price : offer.MaxStack;
        if (affordable < 1)
        {
            _notice.Show(ClientText.Get(GameTextId.NotEnoughGold));
            return;
        }

        _countBox.Open(ClientText.Get("Count.HowManyItems"), ClientText.Get("Count.ToBuy"), Math.Min(offer.MaxStack, affordable), count => BuyFromShop(offer, count));
    }

    private void BuyFromShop(ShopOffer offer, int count) =>
        _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Buy, _state?.InventoryRevision ?? 0, offer.Code,
            count: count);

    // As in the original, a stack asks how many of it to sell; an item sells whole.
    private void SellToShop(OwnedItemView item)
    {
        if (item.Count <= 1)
        {
            SellToShop(item, 1);
            return;
        }

        _countBox.Open(ClientText.Get("Count.HowManyItems"), ClientText.Get("Count.ToSell"), item.Count, count => SellToShop(item, count));
    }

    private void SellToShop(OwnedItemView item, int count) =>
        _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Sell, _state?.InventoryRevision ?? 0,
            itemId: item.InstanceId, count: count);

    private bool InterceptInventoryClick(OwnedItemView item)
    {
        // Shift and a click links the item into an open chat line.
        var keyboard = Keyboard.GetState();
        if ((keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift)) && _chat.LinkItem(item))
        {
            return true;
        }

        if (InterceptTradeClick(item) || _personalShop.InterceptBagClick(item) || _itemBox.InterceptBagClick(item))
        {
            return true;
        }

        if (BenchHolds(item.InstanceId))
        {
            return true;
        }

        if (!_shop.FullyOpen || _shop.Mode == HudShopMode.Buy || _state is null)
        {
            return false;
        }

        if (_shop.Mode == HudShopMode.Sell && item.Slot >= 0)
        {
            return true;
        }

        if (_shop.Mode == HudShopMode.Sell)
        {
            SellToShop(item);
            return true;
        }

        _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Repair, _state.InventoryRevision, itemId: item.InstanceId);
        return true;
    }

    // An item set down above the inventory with no window to take it goes to the ground, as in the original: not while a
    // shop, the warehouse, a work window or a travel window is open, nor an item the rules keep; a stack asks how many.
    private bool ThrowFromInventory(OwnedItemView item)
    {
        if (ServiceWindowOpen(blockingOnly: false) || _wingGate.IsOpen || _itemUse.Teleport.IsOpen || !ItemDropRules.CanDrop(item.Kind, item.CatalogKey))
        {
            return false;
        }

        if (item.Count == 1)
        {
            _inventory.Drop(item, 1);
            return true;
        }

        _countBox.Open(ClientText.Get("Count.HowManyItems"), ClientText.Get("Count.ToDrop"), item.Count, count => _inventory.Drop(item, count));
        return true;
    }

    // The first line of the count box's gold questions; the second says what the gold is for.

    // The coin by the gold: with the warehouse open it stores gold there (as much as is carried and the warehouse still
    // holds), otherwise it drops gold on the ground, not while a work window or the wing gate is open, as the original
    // refuses it; each asks how much.
    private void GoldFromInventory()
    {
        if (_state is null)
        {
            return;
        }

        if (_warehouse.FullyOpen && _warehouse.View is { } view && _services.Open == NpcServiceKind.Warehouse)
        {
            _countBox.Open(ClientText.Get("Count.HowMuchGold"), ClientText.Get("Count.ToStore"), Math.Min(_state.Money, WarehouseStorage.MaxMoney - view.Money),
                amount => _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.DepositGold,
                    _state?.InventoryRevision ?? 0, amount: amount));
            return;
        }

        if (!ServiceWindowOpen(blockingOnly: true) && !_wingGate.IsOpen)
        {
            _countBox.Open(ClientText.Get("Count.HowMuchGold"), ClientText.Get("Count.ToDrop"), _state.Money, amount => _inventory.Drop(null, amount));
        }
    }

    // An inventory item set down on the shop is sold; on the warehouse grid it is stored where HudWarehouseWindow places it.
    private bool DropFromInventory(Guid itemId, Point pointer)
    {
        if (_state is null)
        {
            return false;
        }

        // Set on an action bar key, the item becomes its shortcut and stays in the bag.
        if (_actionBar.TakeItem(Layout(), pointer, OwnedItemView.Find(_state.Items, itemId)))
        {
            return true;
        }

        if (_shop.FullyOpen && _shop.Contains(pointer))
        {
            if (OwnedItemView.Find(_state.Items, itemId) is { } sold)
            {
                SellToShop(sold);
            }

            return true;
        }

        var dropped = OwnedItemView.Find(_state.Items, itemId);
        if (_benches.FirstOrDefault(static bench => bench.FullyOpen)?.Place(dropped, pointer, _state) is { } refusal)
        {
            if (refusal.Length > 0)
            {
                _notice.Show(refusal);
            }

            return true;
        }

        // Over the grid: free cells store it, one stored item is exchanged; a refusal keeps it on the cursor.
        if (Storage.SetDownOnGrid(dropped, pointer) is { Kind: not HudWarehouseActionKind.None } action)
        {
            ApplyWarehouseAction(_state, action);
            return action.Kind == HudWarehouseActionKind.Deposit;
        }

        return false;
    }

    private void ApplyWarehouseAction(OwnerState state, HudWarehouseAction action)
    {
        var revision = state?.InventoryRevision ?? 0;
        switch (action.Kind)
        {
            case HudWarehouseActionKind.Exit:
                CloseServices();
                break;
            case HudWarehouseActionKind.Lifted:
                if (_itemKeys.GetValueOrDefault(action.ItemId) is { } key)
                {
                    ItemSound?.Invoke(key);
                }

                break;
            case HudWarehouseActionKind.Deposit or HudWarehouseActionKind.Move or HudWarehouseActionKind.Withdraw:
                var service = action.Kind switch
                {
                    HudWarehouseActionKind.Deposit => NpcServiceAction.Deposit,
                    HudWarehouseActionKind.Move => NpcServiceAction.Move,
                    _ => NpcServiceAction.Withdraw
                };
                _services.Request(_services.NpcId, _services.NpcName, StorageKind, service, revision, itemId: action.ItemId,
                    anchor: action.Anchor, covered: action.Covered);
                break;
            case HudWarehouseActionKind.WithdrawGold when state is not null && _warehouse.View is { } view && _services.Open == NpcServiceKind.Warehouse:
                // As much as is stored and the character may still carry, as the original bounds the count.
                _countBox.Open(ClientText.Get("Count.HowMuchGold"), ClientText.Get("Count.ToTakeOut"), Math.Min(view.Money, MoneyLimits.CarryLimit(state.Level, state.Tier) - state.Money),
                    amount => _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.WithdrawGold,
                        _state?.InventoryRevision ?? 0, amount: amount));
                break;
            case HudWarehouseActionKind.Refused:
                _notice.Show(action.Message);
                break;
            case HudWarehouseActionKind.ClanLog:
                _sendClan?.Invoke(Protocol.Packets.ClanAction.WarehouseLog, 0, "");
                break;
            case HudWarehouseActionKind.ClanPermissions:
                _sendClan?.Invoke(Protocol.Packets.ClanAction.SetPermissions, (ulong)action.Anchor, "");
                break;
            case HudWarehouseActionKind.ClanBuff when state is not null && OwnedItemView.Find(state.Items, action.ItemId) is { } buffItem:
                _itemUse.Request(buffItem, state);
                break;
            case HudWarehouseActionKind.ClanCoinBuff:
                _sendClan?.Invoke(Protocol.Packets.ClanAction.SpendCoins, (ulong)action.Anchor, "");
                break;
            case HudWarehouseActionKind.ClanDonateCoins:
                // At most the coins the account holds (as last learned; the server checks the account) and one donation may give.
                var coinKeeper = _services.NpcId;
                Coins.Refresh();
                _countBox.Open(ClientText.Get("ClanWarehouse.DonateCoinsTitle"), ClientText.Get("ClanWarehouse.DonateCoinsPrompt"),
                    Coins.Value > 0 ? Math.Min(Coins.Value, Simulation.Social.Clans.ClanBuffs.MaxCoinDonation) : Simulation.Social.Clans.ClanBuffs.MaxCoinDonation,
                    amount => _donateClanCoins?.Invoke(coinKeeper, amount));
                break;
            case HudWarehouseActionKind.ClanUpgrade:
                _sendClan?.Invoke(Protocol.Packets.ClanAction.Upgrade, (ulong)action.Anchor, "");
                break;
            case HudWarehouseActionKind.ClanDonate when state is not null:
                // At most what the player carries and one donation may give (ClanUpgrades).
                var keeper = _services.NpcId;
                _countBox.Open(ClientText.Get("ClanWarehouse.DonateTitle"), ClientText.Get("ClanWarehouse.DonatePrompt"),
                    Math.Min(state.Money, Simulation.Social.Clans.ClanUpgrades.MaxDonation),
                    amount => _donateClan?.Invoke(keeper, amount));
                break;
        }
    }

    private bool BenchHolds(Guid id)
    {
        foreach (var bench in _benches)
        {
            if (bench.Holds(id))
            {
                return true;
            }
        }

        return false;
    }

    private void OpenBench(Action show)
    {
        _npcOpen = false;
        show();
        RequestIcons(_state);
        OpenOnly(_inventorySlide);
    }

    private void ApplyWorkAction(HudWorkbench bench, OwnerState state, HudWorkActionKind action)
    {
        switch (action)
        {
            case HudWorkActionKind.Exit:
                CloseServices();
                break;
            case HudWorkActionKind.Start when state is not null:
                if (state.Money < bench.Price(state))
                {
                    _notice.Show(ClientText.Get(GameTextId.NotEnoughGold));
                    break;
                }

                bench.Started(state);
                _services.Request(_services.NpcId, _services.NpcName, bench.Service, bench.WorkAction, state.InventoryRevision, itemId: bench.Item,
                    materials: bench.Materials);
                break;
        }
    }

    // The result of the work the bench sent last, once it arrived; a refusal without a result ends the craft as failed.
    private void ShowWorkResult(HudWorkbench bench)
    {
        if (_services.LastCommand is not { } command || command.Action != bench.WorkAction)
        {
            return;
        }

        if (_services.LastWork is not { } work)
        {
            if (!_services.LastOk)
            {
                CraftResult?.Invoke(false);
            }

            return;
        }

        _notice.Show(bench.ResultText(work.Result));
        CraftResult?.Invoke(bench.Succeeded(work.Result));
        CloseServices();
    }

    // Where a warehouse item carried over the inventory window would land in the bag.
    private HudPlacement StoredPlacement(OwnedItemView carried, Point pointer) =>
        _inventorySlide.FullyOpen && _state is not null
            ? HudInventoryPlacement.OfStored(PanelOrigin(Layout(), _inventorySlide), _state.Items, carried, pointer)
            : default;

    private string InventoryNote(OwnedItemView item)
    {
        if (!_shop.FullyOpen || _services.QuoteFor(item.InstanceId) is not { } quote)
        {
            return "";
        }

        if (_shop.Mode == HudShopMode.Repair)
        {
            return quote.RepairCost > 0 ? ClientText.Format("Shop.RepairCost", quote.RepairCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)) : ClientText.Get("Shop.NotDamaged");
        }

        return quote.SellPrice >= 0 ? ClientText.Format("Shop.SellsFor", quote.SellPrice.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)) : ClientText.Get("Shop.DoesNotBuy");
    }

    private Task<PristonTale.Simulation.Items.Usable.ItemUseOutcome> UseItemAsync(PristonTale.Simulation.Items.Usable.ItemUseCommand command) =>
        UseItem is { } use ? use(command)
            : Task.FromResult(PristonTale.Simulation.Items.Usable.ItemUseOutcome.Refused(command.ExpectedRevision,
                PristonTale.Simulation.Items.Usable.ItemUseStatus.NotUsable));
}

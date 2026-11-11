using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Gameplay.Localization;
using PristonTale.Contracts.Texts;

namespace PristonTale.Gameplay.UI.PersonalShops;

// The personal shop on the HUD, as the original runs it. The inventory's shop button opens the player's own listing in a
// town: while it does not sell, a click on a bag item asks its price per unit and lays it out (a listed item keeps its
// place in the bag), a click on a listed item asks to take it back, and the start button asks for the sign and opens the
// shop. While it sells nothing in the bag moves and the player stands still; the window may close, and a button at the
// left reopens it. Another player's shop opens when its seller is reached: a click on an offer asks how many (a stack) or
// whether to buy it.
public sealed class HudPersonalShopController(IClientLog log)
{
    // How long an opened shop waits for its sign (the player's description) before it counts as closed again: 300 of the
    // game's ticks.
    private const double SignWaitMs = 300 * 1000d / PristonTale.Contracts.Units.GameUnits.TicksPerSecond;
    private static readonly Rectangle ReopenBounds = new(10, 220, 70, 70);

    private readonly HudRequest<PersonalShopReply> _request = new(log, "Personal shop request failed");
    private readonly HudSignBox _sign = new();
    private PersonalShopOffer _confirm;

    // A bag item being priced: its seller first chooses gold, coins or view only.
    private OwnedItemView _pricing;
    private static readonly string[] CurrencyNames = ["Gold", "Coins", "View only"];
    private static readonly PersonalShopCurrency[] Currencies = [PersonalShopCurrency.Gold, PersonalShopCurrency.Coins, PersonalShopCurrency.ViewOnly];
    private static readonly Color ChoiceColor = new(52, 36, 24);
    private static readonly Color ChoiceLitColor = new(110, 80, 40);
    private static readonly Color ChoiceRimColor = new(170, 130, 70);
    private OwnerState _state;
    private long _followed = -1;
    private PersonalShopAction _sent;
    private ulong _sentTo;
    private double _nowMs;
    private double _signWaitUntilMs;

    public HudPersonalShopWindow Window { get; } = new();
    public IPersonalShopClient Client { get; set; }
    public Action<string> Notify { get; set; } = static _ => { };

    // Sends a chat command ("/stall" to leave the selling shop as an offline stall).
    public Action<string> Command { get; set; } = static _ => { };

    // The count box: two lines, the largest number, what to do with the number accepted.
    public Action<string, string, long, Action<int>> AskCount { get; set; } = static (_, _, _, _) => { };

    // The player's own sign hangs over it (set by the HUD every frame).
    public bool SignShown { get; set; }

    // The player's own shop sells: its sign hangs, or an opened shop waits for it.
    public bool Selling => SignShown || _nowMs < _signWaitUntilMs;

    public bool ModalOpen => _sign.IsOpen || _confirm is not null || _pricing is not null;

    // The seller's visits this play: shops whose sign is drawn in the visited colour.
    public HashSet<ulong> Visited { get; } = [];
    public bool SignOpen => _sign.IsOpen;
    private bool ReopenShown => Selling && !Window.IsOpen;

    public bool Holds(Guid itemId) => Window.Holds(itemId);

    public bool Captures(Point pointer) => ModalOpen || ReopenShown && ReopenBounds.Contains(pointer);

    public void OpenOwn(bool inTown)
    {
        if (!inTown && !Selling)
        {
            Notify(ClientText.Format(PersonalShopMessages.Of(PersonalShopStatus.NotInTown)));
            return;
        }

        Window.ShowOwn();
    }

    public void View(ulong sellerId)
    {
        if (!Selling && Client is { } client)
        {
            Send(PersonalShopAction.View, sellerId, () => client.ViewPersonalShopAsync(sellerId));
        }
    }

    public void Update(OwnerState state, double nowMs)
    {
        _state = state;
        _nowMs = nowMs;
        if (SignShown)
        {
            _signWaitUntilMs = 0;
        }

        if (state is not null && state.InventoryRevision != _followed)
        {
            _followed = state.InventoryRevision;
            Window.Follow(state.Items);
        }

        if (_request.TryTake(out var reply))
        {
            Apply(reply);
        }

        Window.Selling = Selling;
    }

    public void Type(ReadOnlySpan<char> typed, Func<Keys, bool> pressed, Func<char, bool> canDraw, Func<string, int> measure) =>
        _sign.Type(typed, pressed, canDraw, measure);

    // A click on a bag item while the player sells (nothing moves) or its own listing shows (it is the listing's).
    public bool InterceptBagClick(OwnedItemView item)
    {
        if (Selling)
        {
            return true;
        }

        if (!Window.IsOpen || Window.Buying)
        {
            return false;
        }

        if (Window.Holds(item.InstanceId) || !item.InBag || _state is not { } state)
        {
            return true;
        }

        if (!PersonalShopRules.IsListable(item.Kind, item.CatalogKey))
        {
            Notify(ClientText.Format(PersonalShopMessages.Of(PersonalShopStatus.NotListable)));
            return true;
        }

        _pricing = item;
        return true;
    }

    // Gold asks a price within what the seller may carry, coins any price up to the limit, view only none.
    private void Price(OwnedItemView item, PersonalShopCurrency currency)
    {
        if (currency == PersonalShopCurrency.ViewOnly)
        {
            List(item, 0, currency);
            return;
        }

        var highest = PersonalShopRules.MaxPrice / Math.Max(1, item.Count);
        if (currency == PersonalShopCurrency.Gold && _state is { } state)
        {
            var room = PersonalShopRules.GoldRoom(state.Level, state.Tier, state.Money, Window.Total);
            highest = Math.Min(highest, room / Math.Max(1, item.Count));
        }

        if (highest < 1)
        {
            Notify(ClientText.Format(PersonalShopMessages.Of(PersonalShopStatus.GoldLimit)));
            return;
        }

        var (first, second) = item.Count > 1
            ? (ClientText.Get("PersonalShop.UnitPrice"), currency == PersonalShopCurrency.Coins ? "(in coins)" : ClientText.Get("PersonalShop.UnitPrice2"))
            : (ClientText.Get("PersonalShop.Register"), currency == PersonalShopCurrency.Coins ? "(in coins)" : ClientText.Get("PersonalShop.Register2"));
        AskCount(first, second, highest, price => List(item, price, currency));
    }

    private void List(OwnedItemView item, long price, PersonalShopCurrency currency)
    {
        if (!Window.TryList(item, price, currency))
        {
            Notify(ClientText.Get("PersonalShop.NoRoom"));
        }
    }

    // The currency buttons of the pricing box: gold, coins, view only.
    public static Rectangle Choice(int index)
    {
        var box = HudMessageBox.Bounds;
        return new Rectangle(box.X + 22 + index * 62, box.Y + 74, 58, 26);
    }

    public bool Click(Point pointer)
    {
        if (_sign.IsOpen)
        {
            _sign.Click(pointer);
            return true;
        }

        if (_pricing is { } pricing)
        {
            for (var index = 0; index < Currencies.Length; index++)
            {
                if (Choice(index).Contains(pointer))
                {
                    _pricing = null;
                    Price(pricing, Currencies[index]);
                    return true;
                }
            }

            if (HudMessageBox.Reject.Contains(pointer))
            {
                _pricing = null;
            }

            return true;
        }

        if (_confirm is { } confirm)
        {
            if (HudMessageBox.Accept.Contains(pointer))
            {
                _confirm = null;
                if (Window.Buying)
                {
                    Buy(confirm, 1);
                }
                else
                {
                    Window.Unlist(confirm.Item.InstanceId);
                }
            }
            else if (HudMessageBox.Reject.Contains(pointer))
            {
                _confirm = null;
            }

            return true;
        }

        if (ReopenShown && ReopenBounds.Contains(pointer))
        {
            Window.ShowOwn();
            return true;
        }

        switch (Window.Click(pointer))
        {
            case HudPersonalShopAction.None:
                return false;
            case HudPersonalShopAction.Exit:
                Window.Hide();
                break;
            case HudPersonalShopAction.Start when !Selling:
                Start();
                break;
            case HudPersonalShopAction.Stop when Selling && Client is { } client:
                Send(PersonalShopAction.Close, 0, client.ClosePersonalShopAsync);
                break;
            case HudPersonalShopAction.Offline when Selling:
                Command("/stall");
                break;
            case HudPersonalShopAction.Unlist when Window.Selected is { } offer:
                _confirm = offer;
                break;
            case HudPersonalShopAction.Buy when Window.Selected is { } offer:
                Ask(offer);
                break;
        }

        return true;
    }

    // Escape closes the question first, then the window; false when nothing was open.
    public bool Close()
    {
        if (_sign.IsOpen || _confirm is not null || _pricing is not null)
        {
            _sign.Close();
            _confirm = null;
            _pricing = null;
            return true;
        }

        if (!Window.IsOpen)
        {
            return false;
        }

        Window.Hide();
        return true;
    }

    public void Draw(HudPainter painter)
    {
        if (ReopenShown)
        {
            painter.Stretch(HudPersonalShopArt.Reopen, ReopenBounds);
        }
    }

    public void DrawModal(HudPainter painter, Point pointer)
    {
        if (_sign.IsOpen)
        {
            _sign.Draw(painter, pointer);
        }
        else if (_pricing is { } pricing)
        {
            HudMessageBox.Draw(painter, "Sell it for:", "", pointer, pricing.Name);
            for (var index = 0; index < Currencies.Length; index++)
            {
                var choice = Choice(index);
                painter.Fill(choice, ChoiceRimColor);
                painter.Fill(new Rectangle(choice.X + 1, choice.Y + 1, choice.Width - 2, choice.Height - 2), choice.Contains(pointer) ? ChoiceLitColor : ChoiceColor);
                painter.TextCentered(CurrencyNames[index], choice.X, choice.Right, choice.Y + 5, Color.White);
            }
        }
        else if (_confirm is { } confirm)
        {
            HudMessageBox.Draw(painter, ClientText.Get("Count.DoYouWant"), ClientText.Get(Window.Buying ? "PersonalShop.BuyItem" : "PersonalShop.CancelRegister"), pointer, confirm.Item.Name);
        }
    }

    private void Start()
    {
        if (Window.Offers.Count == 0)
        {
            Notify(ClientText.Get("PersonalShop.NothingToSell"));
            return;
        }

        _sign.Open(sign =>
        {
            if (Client is { } client)
            {
                var entries = Window.Entries();
                Send(PersonalShopAction.Open, 0, () => client.OpenPersonalShopAsync(sign, entries));
            }
        });
    }

    // A view-only item is not sold; a stack asks how many, up to what the gold pays for (coins the server checks); one item
    // asks whether to buy it.
    private void Ask(PersonalShopOffer offer)
    {
        if (offer.Currency == PersonalShopCurrency.ViewOnly)
        {
            Notify(PersonalShopMessages.ViewOnlyText);
            return;
        }

        var affordable = offer.Currency == PersonalShopCurrency.Coins ? offer.Item.Count
            : offer.UnitPrice > 0 ? (_state?.Money ?? 0) / offer.UnitPrice : 0;
        if (affordable < 1)
        {
            Notify(ClientText.Format(PersonalShopMessages.Of(PersonalShopStatus.NotEnoughGold)));
            return;
        }

        if (offer.Item.Count > 1)
        {
            AskCount(ClientText.Get("Count.HowManyItems"), ClientText.Get("Count.ToBuy"), Math.Min(offer.Item.Count, affordable), count => Buy(offer, count));
            return;
        }

        _confirm = offer;
    }

    private void Buy(PersonalShopOffer offer, int count)
    {
        if (Client is { } client && _state is { } state)
        {
            var purchase = new PersonalShopPurchase(Guid.NewGuid(), Window.SellerId, offer.Item.InstanceId, offer.UnitPrice, count, state.InventoryRevision);
            Send(PersonalShopAction.Buy, purchase.SellerId, () => client.BuyFromPersonalShopAsync(purchase));
        }
    }

    private void Send(PersonalShopAction action, ulong sellerId, Func<Task<PersonalShopReply>> send)
    {
        if (_request.Pending)
        {
            Notify(ClientText.Format(PersonalShopMessages.Of(PersonalShopStatus.Busy)));
            return;
        }

        (_sent, _sentTo) = (action, sellerId);
        _request.Start(send, _ => new PersonalShopReply(new PersonalShopOutcome(false, PersonalShopStatus.Rejected, 0, GameText.None)));
    }

    // A view shows the shop unless the player's own listing took the window meanwhile; a purchase refreshes the shop it was
    // made in while it still shows, or closes it. An opened shop sells until its sign shows; a refusal says why.
    private void Apply(PersonalShopReply reply)
    {
        var shown = Window.IsOpen && Window.Buying && Window.SellerId == _sentTo;
        if (_sent == PersonalShopAction.View && reply.Listing is { } listing && (!Window.IsOpen || Window.Buying))
        {
            Window.Show(listing);
            Visited.Add(listing.SellerId);
        }
        else if (_sent == PersonalShopAction.Buy && shown)
        {
            if (reply.Listing is { } current)
            {
                Window.Show(current);
            }
            else if (reply.Outcome.Ok || reply.Outcome.Status is PersonalShopStatus.Closed or PersonalShopStatus.OutOfReach)
            {
                Window.Hide();
            }
        }
        else if (_sent == PersonalShopAction.Open && reply.Outcome.Ok)
        {
            _signWaitUntilMs = _nowMs + SignWaitMs;
        }
        else if (_sent == PersonalShopAction.Close && reply.Outcome.Ok)
        {
            _signWaitUntilMs = 0;
        }

        if (!reply.Outcome.Ok)
        {
            // A refusal without a line is the client's own: the server was not reached.
            Notify(reply.Outcome.Message.IsNone ? HudRequest.ConnectionError : ClientText.Format(reply.Outcome.Message));
        }
    }
}

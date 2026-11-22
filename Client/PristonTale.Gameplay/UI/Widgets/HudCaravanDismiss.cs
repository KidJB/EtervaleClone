using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// A right click on the caravan's row of the Active Buffs box asks whether to send the caravan away before its time; Yes asks
// the server to dismiss it (its storage keeps the items for the next caravan).
public sealed class HudCaravanDismiss
{
    private string _name = "";

    public bool Confirming { get; private set; }

    public bool Captures(Point pointer) => Confirming && HudMessageBox.Bounds.Contains(pointer);

    public void Offer(string caravanName)
    {
        _name = caravanName;
        Confirming = true;
    }

    // True when the box took the click; dismissed is true when Yes was clicked.
    public bool Click(Point pointer, out bool dismissed)
    {
        dismissed = false;
        if (!Confirming)
        {
            return false;
        }

        if (HudMessageBox.Accept.Contains(pointer))
        {
            dismissed = Accept();
        }
        else if (HudMessageBox.Reject.Contains(pointer))
        {
            Confirming = false;
        }

        return true;
    }

    // Yes from the keyboard (Enter): true when a question was open.
    public bool Accept()
    {
        var answered = Confirming;
        Confirming = false;
        return answered;
    }

    public void Close() => Confirming = false;

    public void Draw(HudPainter painter, Point pointer)
    {
        if (!Confirming)
        {
            return;
        }

        painter.Flush();
        HudMessageBox.Draw(painter, ClientText.Get("Caravan.Dismiss"), ClientText.Format("Caravan.DismissAsk", _name), pointer);
    }
}

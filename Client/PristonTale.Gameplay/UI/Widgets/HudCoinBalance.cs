namespace PristonTale.Gameplay.UI.Widgets;

// The account's coins as the HUD last learned them: asked of the server when a window that shows them opens (Refresh), or
// set by an answer that carried them (a coin shop purchase). Poll reads a finished query without waiting.
public sealed class HudCoinBalance
{
    private Task<long> _query;

    public Func<Task<long>> Request { get; set; }

    public long Value { get; private set; }

    public string Text { get; private set; } = "0";

    public void Refresh()
    {
        if (_query is null && Request is { } request)
        {
            _query = request();
        }
    }

    public void Poll()
    {
        if (_query is not { IsCompleted: true } query)
        {
            return;
        }

        _query = null;
        if (query.IsCompletedSuccessfully)
        {
            Set(query.Result);
        }
    }

    public void Set(long value)
    {
        if (value != Value || Text.Length == 0)
        {
            Value = value;
            Text = value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

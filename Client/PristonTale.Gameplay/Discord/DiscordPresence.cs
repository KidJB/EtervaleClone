using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Gameplay.Discord;

// The player's Discord profile line, kept by one background task so the game loop never waits on Discord. The screens set
// the activity they show; the task connects when Discord runs (again after it restarts), sends the latest activity at most
// every UpdateIntervalMs (Discord drops faster updates) and clears it when the game closes.
public sealed class DiscordPresence : IAsyncDisposable
{
    // The game's Discord application: its name and art assets are what profiles show.
    public const string ApplicationId = "1148603759740604537";

    private const int UpdateIntervalMs = 15_000;
    private const int RetryIntervalMs = 30_000;
    private const int PollIntervalMs = 1_000;

    private readonly string _applicationId;
    private readonly IDiscordTransport _transport;
    private readonly IClientLog _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private DiscordActivity _wanted;

    public DiscordPresence(string applicationId, IDiscordTransport transport, IClientLog log)
    {
        _applicationId = applicationId;
        _transport = transport;
        _log = log;
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    // When the game started, for the elapsed counter every activity carries.
    public long StartUnixSeconds { get; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // What to show next; the same activity again changes nothing.
    public void Show(DiscordActivity activity) => Volatile.Write(ref _wanted, activity);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var stream = await _transport.ConnectAsync(ct);
                if (stream is not null)
                {
                    await ServeAsync(stream, ct);
                }
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException or ObjectDisposedException or System.Net.Sockets.SocketException)
            {
                _log.Warn($"[Discord] Connection lost: {failure.Message}");
            }

            await Task.Delay(RetryIntervalMs, ct);
        }
    }

    // One connection: the handshake, then the activity whenever it changed, until Discord closes or the game stops.
    private async Task ServeAsync(Stream stream, CancellationToken ct)
    {
        await DiscordIpc.WriteAsync(stream, DiscordIpc.Handshake, DiscordIpc.HandshakePayload(_applicationId), ct);
        if (await DiscordIpc.ReadAsync(stream, ct) is not { Opcode: DiscordIpc.Frame })
        {
            _log.Warn("[Discord] The handshake was refused; check DiscordPresence.ApplicationId.");
            return;
        }

        var replies = DrainAsync(stream, ct);
        DiscordActivity sent = null;
        var sentAt = 0L;
        try
        {
            while (!ct.IsCancellationRequested && !replies.IsCompleted)
            {
                var wanted = Volatile.Read(ref _wanted);
                if (wanted is not null && wanted != sent && (sent is null || Environment.TickCount64 - sentAt >= UpdateIntervalMs))
                {
                    await SendAsync(stream, wanted, ct);
                    (sent, sentAt) = (wanted, Environment.TickCount64);
                }

                await Task.Delay(PollIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ClearAsync(stream);
            throw;
        }
    }

    private static Task SendAsync(Stream stream, DiscordActivity activity, CancellationToken ct) =>
        DiscordIpc.WriteAsync(stream, DiscordIpc.Frame, DiscordIpc.ActivityPayload(activity, Environment.ProcessId, Guid.NewGuid().ToString("N")), ct);

    // The profile line goes when the game closes; a Discord already gone leaves nothing to clear.
    private static async Task ClearAsync(Stream stream)
    {
        try
        {
            using var timeout = new CancellationTokenSource(1000);
            await SendAsync(stream, null, timeout.Token);
            await DiscordIpc.WriteAsync(stream, DiscordIpc.Close, [], timeout.Token);
        }
        catch (Exception failure) when (failure is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    // Discord answers every command; its replies are read and dropped so its side never stalls on a full pipe.
    private static async Task DrainAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            while (await DiscordIpc.ReadAsync(stream, ct) is { } frame && frame.Opcode != DiscordIpc.Close)
            {
            }
        }
        catch (Exception failure) when (failure is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
        {
        }
    }
}

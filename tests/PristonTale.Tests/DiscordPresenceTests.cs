using System.IO.Pipes;
using System.Text.Json;
using PristonTale.Contracts.Characters;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Discord;

namespace PristonTale.Tests;

// The Discord profile line: the IPC frames, the activity text, and a presence that hands its activity to a listening Discord.
internal static class DiscordPresenceTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("discord: frames carry the opcode and length, the handshake names the application, an activity its lines and start", FramesAsync);
        yield return ("discord: the profile reads where the player is, alone or in a party, and it is on by default with the game's numeric application id", TextAsync);
        yield return ("discord: a presence handshakes with Discord, sends the activity and clears it when the game closes", PresenceAsync);
    }

    private static async Task FramesAsync()
    {
        using var stream = new MemoryStream();
        await DiscordIpc.WriteAsync(stream, DiscordIpc.Handshake, DiscordIpc.HandshakePayload("123"), CancellationToken.None);
        stream.Position = 0;
        var frame = await DiscordIpc.ReadAsync(stream, CancellationToken.None);
        Assert.True(frame is { Opcode: DiscordIpc.Handshake } read && read.Json == "{\"v\":1,\"client_id\":\"123\"}", $"the handshake frame is wrong: {frame}");
        Assert.True(await DiscordIpc.ReadAsync(stream, CancellationToken.None) is null, "a closed stream gave a frame");

        var activity = new DiscordActivity("Ricarten", "Solo - Ann (Lv 10)", "logo", "Fighter", 1000);
        using var json = JsonDocument.Parse(DiscordIpc.ActivityPayload(activity, 42, "n1"));
        var root = json.RootElement;
        var shown = root.GetProperty("args").GetProperty("activity");
        Assert.True(root.GetProperty("cmd").GetString() == "SET_ACTIVITY" && root.GetProperty("args").GetProperty("pid").GetInt32() == 42
            && shown.GetProperty("details").GetString() == "Ricarten" && shown.GetProperty("timestamps").GetProperty("start").GetInt64() == 1000
            && shown.GetProperty("assets").GetProperty("large_text").GetString() == "Fighter", "the activity payload is wrong");
        var longText = new string('x', 300);
        using var cut = JsonDocument.Parse(DiscordIpc.ActivityPayload(activity with { State = longText }, 1, "n2"));
        Assert.True(cut.RootElement.GetProperty("args").GetProperty("activity").GetProperty("state").GetString()!.Length <= 128, "a long line was not cut to 128 bytes");
        using var cleared = JsonDocument.Parse(DiscordIpc.ActivityPayload(null, 1, "n3"));
        Assert.True(cleared.RootElement.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Null, "a cleared activity is not null");
    }

    private static Task TextAsync()
    {
        var solo = DiscordPresenceText.Playing(5, "Ann", PlayerClass.Fighter, 30, "Ricarten Town", 1);
        var party = DiscordPresenceText.Playing(5, "Ann", PlayerClass.Fighter, 30, "", 3);
        Assert.True(solo is { Details: "Ricarten Town", State: "Solo - Ann (Lv 30)", LargeImageText: "Fighter", StartUnixSeconds: 5 }, $"the solo line is wrong: {solo}");
        Assert.True(party is { Details: "Etervale", State: "Party (3/6) - Ann (Lv 30)" }, $"the party line is wrong: {party}");
        Assert.True(DiscordPresence.ApplicationId.All(char.IsAsciiDigit) && new ClientSettings().DiscordPresence, "the presence is off by default or its id is not numeric");
        return Task.CompletedTask;
    }

    // A Discord stand-in on an in-process pipe pair: it answers the handshake and records the frames it reads.
    private sealed class PipeTransport(Stream client) : IDiscordTransport
    {
        private int _used;

        public Task<Stream> ConnectAsync(CancellationToken ct) => Task.FromResult(Interlocked.Exchange(ref _used, 1) == 0 ? client : null!);
    }

    private static async Task PresenceAsync()
    {
        using var server = new AnonymousPipeServerStream(PipeDirection.In);
        using var reply = new AnonymousPipeServerStream(PipeDirection.Out);
        await using var clientStream = new DuplexStream(new AnonymousPipeClientStream(PipeDirection.Out, server.ClientSafePipeHandle),
            new AnonymousPipeClientStream(PipeDirection.In, reply.ClientSafePipeHandle));
        await using var discordStream = new DuplexStream(reply, server);
        var presence = new DiscordPresence("123", new PipeTransport(clientStream), NullClientLog.Instance);
        presence.Show(DiscordPresenceText.Menu(presence.StartUnixSeconds, "Choosing a character"));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var handshake = await DiscordIpc.ReadAsync(discordStream, timeout.Token);
        Assert.True(handshake is { Opcode: DiscordIpc.Handshake } hello && hello.Json.Contains("\"client_id\":\"123\"", StringComparison.Ordinal), "no handshake");
        await DiscordIpc.WriteAsync(discordStream, DiscordIpc.Frame, "{\"cmd\":\"DISPATCH\",\"evt\":\"READY\"}"u8.ToArray(), timeout.Token);
        var update = await DiscordIpc.ReadAsync(discordStream, timeout.Token);
        Assert.True(update is { Opcode: DiscordIpc.Frame } set && set.Json.Contains("Choosing a character", StringComparison.Ordinal), $"no activity: {update}");

        var closing = presence.DisposeAsync().AsTask();
        var clear = await DiscordIpc.ReadAsync(discordStream, timeout.Token);
        Assert.True(clear is { Opcode: DiscordIpc.Frame } cleared && cleared.Json.Contains("\"activity\":null", StringComparison.Ordinal), $"the activity was not cleared: {clear}");
        await closing.WaitAsync(timeout.Token);
    }

    // Writes to one pipe and reads from another, as a duplex Discord connection does.
    private sealed class DuplexStream(Stream write, Stream read) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => write.Flush();
        public override int Read(byte[] buffer, int offset, int count) => read.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => read.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => write.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => write.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                write.Dispose();
                read.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

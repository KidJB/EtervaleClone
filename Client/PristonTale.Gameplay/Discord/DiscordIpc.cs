using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PristonTale.Gameplay.Discord;

// What the player's Discord profile shows: two lines, the large picture (a key of the Discord application's art assets) with
// its hover text, and the time the game started (Unix seconds) for the elapsed counter.
public sealed record DiscordActivity(string Details, string State, string LargeImageKey, string LargeImageText, long StartUnixSeconds);

// Discord's local RPC protocol, as the desktop client listens for it: frames of {int32 opcode, int32 length} (little-endian)
// then that many bytes of UTF-8 JSON. A connection opens with a handshake naming the application, then sends commands.
public static class DiscordIpc
{
    public const int Handshake = 0;
    public const int Frame = 1;
    public const int Close = 2;
    public const int MaxPipes = 10;
    private const int HeaderSize = 8;
    private const int MaxPayload = 64 * 1024;
    private const int MaxTextBytes = 128;

    public static byte[] HandshakePayload(string applicationId) =>
        Json(writer =>
        {
            writer.WriteNumber("v", 1);
            writer.WriteString("client_id", applicationId);
        });

    // SET_ACTIVITY for this process; a null activity clears the profile's game line.
    public static byte[] ActivityPayload(DiscordActivity activity, int processId, string nonce) =>
        Json(writer =>
        {
            writer.WriteString("cmd", "SET_ACTIVITY");
            writer.WriteStartObject("args");
            writer.WriteNumber("pid", processId);
            if (activity is null)
            {
                writer.WriteNull("activity");
            }
            else
            {
                writer.WriteStartObject("activity");
                writer.WriteString("details", Limit(activity.Details));
                writer.WriteString("state", Limit(activity.State));
                writer.WriteStartObject("timestamps");
                writer.WriteNumber("start", activity.StartUnixSeconds);
                writer.WriteEndObject();
                writer.WriteStartObject("assets");
                writer.WriteString("large_image", Limit(activity.LargeImageKey));
                writer.WriteString("large_text", Limit(activity.LargeImageText));
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteString("nonce", nonce);
        });

    public static async Task WriteAsync(Stream stream, int opcode, byte[] payload, CancellationToken ct)
    {
        var frame = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, HeaderSize);
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    // One frame: its opcode and JSON text; null when the other side closed.
    public static async Task<(int Opcode, string Json)?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[HeaderSize];
        if (!await FillAsync(stream, header, ct))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > MaxPayload)
        {
            throw new InvalidDataException("Discord sent a frame of an impossible length.");
        }

        var payload = new byte[length];
        return await FillAsync(stream, payload, ct) ? (BinaryPrimitives.ReadInt32LittleEndian(header), Encoding.UTF8.GetString(payload)) : null;
    }

    private static async Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (count == 0)
            {
                return false;
            }

            read += count;
        }

        return true;
    }

    // Discord refuses a text over 128 bytes; a longer one is cut at a whole character.
    private static string Limit(string text)
    {
        text ??= "";
        if (Encoding.UTF8.GetByteCount(text) <= MaxTextBytes)
        {
            return text;
        }

        var length = text.Length;
        while (length > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > MaxTextBytes - 3)
        {
            length--;
        }

        return string.Concat(text.AsSpan(0, length), "...");
    }

    private static byte[] Json(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}

// Where the Discord desktop client listens; each platform has its own kind of endpoint.
public interface IDiscordTransport
{
    // A stream to the first Discord endpoint that answers, or null when Discord is not running.
    Task<Stream> ConnectAsync(CancellationToken ct);
}

public static class DiscordTransports
{
    public static IDiscordTransport ForThisPlatform() => OperatingSystem.IsWindows() ? new DiscordPipeTransport() : new DiscordSocketTransport();
}

// Windows: the named pipes discord-ipc-0 to discord-ipc-9.
public sealed class DiscordPipeTransport : IDiscordTransport
{
    private const int ConnectTimeoutMs = 500;

    public async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        for (var index = 0; index < DiscordIpc.MaxPipes; index++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{index}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(ConnectTimeoutMs, ct);
                return pipe;
            }
            catch (Exception failure) when (failure is TimeoutException or IOException or UnauthorizedAccessException)
            {
                await pipe.DisposeAsync();
            }
        }

        return null;
    }
}

// Linux and macOS: the Unix sockets discord-ipc-0 to discord-ipc-9, in the user's temporary folder (macOS), the per-user
// runtime folders under /run/user (Linux) and their Flatpak and Snap subfolders.
public sealed class DiscordSocketTransport : IDiscordTransport
{
    private static readonly string[] Subfolders = ["", "app/com.discordapp.Discord", "snap.discord"];

    public async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        foreach (var folder in Folders())
        {
            foreach (var subfolder in Subfolders)
            {
                for (var index = 0; index < DiscordIpc.MaxPipes; index++)
                {
                    var path = Path.Combine(folder, subfolder, $"discord-ipc-{index}");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException)
                    {
                        socket.Dispose();
                    }
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> Folders()
    {
        yield return Path.GetTempPath();
        yield return "/tmp";
        if (Directory.Exists("/run/user"))
        {
            foreach (var user in Directory.EnumerateDirectories("/run/user"))
            {
                yield return user;
            }
        }
    }
}

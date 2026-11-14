using System.Buffers.Binary;
using StbImageSharp;

namespace PristonTale.Engine3D.IO;

// A silent video stored as compressed images, so the client plays it without a video codec on any platform. Layout,
// little-endian: "ETVD", version, width, height, frames per second, frame count (int32 each), then every frame as its
// byte length (int32) and its JPEG or PNG bytes. AssetDump --pack-video writes it from images a movie was cut into.
public readonly record struct FrameVideoHeader(int Width, int Height, int FramesPerSecond, int FrameCount)
{
    public const int Version = 1;
    public const int Size = 24;
    private const int MaximumDimension = 4096;
    private static ReadOnlySpan<byte> Magic => "ETVD"u8;

    public static FrameVideoHeader Read(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[Size];
        stream.ReadExactly(bytes);
        if (!bytes[..4].SequenceEqual(Magic) || BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]) != Version)
        {
            throw new InvalidDataException("Not a frame video of this version.");
        }

        var header = new FrameVideoHeader(BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]));
        if (header.Width is < 1 or > MaximumDimension || header.Height is < 1 or > MaximumDimension || header.FramesPerSecond is < 1 or > 120 || header.FrameCount < 1)
        {
            throw new InvalidDataException($"Frame video header out of range: {header}.");
        }

        return header;
    }

    public void Write(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[Size];
        Magic.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], Version);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], Height);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], FramesPerSecond);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[20..], FrameCount);
        stream.Write(bytes);
    }
}

// Reads a frame video from its start, decoding each frame to RGBA bytes and starting over after the last one.
public sealed class FrameVideoReader : IDisposable
{
    private const int MaximumFrameBytes = 16 * 1024 * 1024;
    private readonly Stream _stream;
    private int _next;

    public FrameVideoReader(Stream stream)
    {
        _stream = stream;
        Header = FrameVideoHeader.Read(stream);
    }

    public FrameVideoHeader Header { get; }

    public byte[] NextFrame()
    {
        if (_next == Header.FrameCount)
        {
            _stream.Position = FrameVideoHeader.Size;
            _next = 0;
        }

        Span<byte> length = stackalloc byte[4];
        _stream.ReadExactly(length);
        var size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size is < 1 or > MaximumFrameBytes)
        {
            throw new InvalidDataException($"Frame {_next} has {size} bytes.");
        }

        var compressed = new byte[size];
        _stream.ReadExactly(compressed);
        _next++;
        var image = ImageResult.FromMemory(compressed, ColorComponents.RedGreenBlueAlpha);
        if (image.Width != Header.Width || image.Height != Header.Height)
        {
            throw new InvalidDataException($"Frame {_next - 1} is {image.Width}x{image.Height}, not {Header.Width}x{Header.Height}.");
        }

        return image.Data;
    }

    public void Dispose() => _stream.Dispose();

    // Packs images of one size, in order, into a frame video.
    public static FrameVideoHeader Pack(IReadOnlyList<string> images, int framesPerSecond, Stream output)
    {
        if (images.Count == 0)
        {
            throw new ArgumentException("No images to pack.", nameof(images));
        }

        FrameVideoHeader header = default;
        Span<byte> length = stackalloc byte[4];
        for (var index = 0; index < images.Count; index++)
        {
            var bytes = File.ReadAllBytes(images[index]);
            var info = ImageInfo.FromStream(new MemoryStream(bytes, writable: false))
                ?? throw new InvalidDataException($"Not an image: {images[index]}");
            if (index == 0)
            {
                header = new FrameVideoHeader(info.Width, info.Height, framesPerSecond, images.Count);
                header.Write(output);
            }
            else if (info.Width != header.Width || info.Height != header.Height)
            {
                throw new InvalidDataException($"{images[index]} is {info.Width}x{info.Height}, not {header.Width}x{header.Height}.");
            }

            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            output.Write(length);
            output.Write(bytes);
        }

        return header;
    }
}

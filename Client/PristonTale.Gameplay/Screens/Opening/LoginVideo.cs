using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;

namespace PristonTale.Gameplay.Screens.Opening;

// The login screen's looping background video (Video/login.etv, a FrameVideo). A thread-pool task reads and decodes
// frames a few ahead of the clock; Update uploads the frame that is due into one texture on the game thread.
internal sealed class LoginVideo : IDisposable
{
    public const string AssetPath = "Video/login.etv";
    private const int FramesAhead = 3;

    private readonly ConcurrentQueue<(long Index, byte[] Pixels)> _decoded = new();
    private readonly SemaphoreSlim _room = new(FramesAhead, FramesAhead);
    private readonly CancellationTokenSource _stop = new();
    private readonly IClientLog _log;
    private readonly Texture2D _texture;
    private readonly Task _decoder;
    private readonly int _framesPerSecond;
    private double _seconds;
    private long _shown = -1;

    private LoginVideo(GraphicsDevice device, FrameVideoReader reader, IClientLog log)
    {
        _log = log;
        _framesPerSecond = reader.Header.FramesPerSecond;
        _texture = new Texture2D(device, reader.Header.Width, reader.Header.Height, false, SurfaceFormat.Color);
        var token = _stop.Token;
        _decoder = Task.Run(() => Decode(reader, token), CancellationToken.None);
    }

    public bool HasFrame => _shown >= 0;

    // The video under assetRoot, or null when it is missing or unreadable (the opening cinematic then plays instead).
    public static LoginVideo Open(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        var path = AssetFiles.Combine(assetRoot, AssetPath);
        if (!AssetFiles.Exists(path))
        {
            return null;
        }

        try
        {
            return new LoginVideo(device, new FrameVideoReader(File.OpenRead(path)), log);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log.Error($"[Login] background video unreadable: {path}", failure);
            return null;
        }
    }

    private void Decode(FrameVideoReader reader, CancellationToken token)
    {
        using (reader)
        {
            try
            {
                for (long index = 0; ; index++)
                {
                    _room.Wait(token);
                    _decoded.Enqueue((index, reader.NextFrame()));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException or InvalidOperationException)
            {
                _log.Error("[Login] background video stopped", failure);
            }
        }
    }

    public void Update(double elapsedSeconds)
    {
        _seconds += elapsedSeconds;
        var due = (long)(_seconds * _framesPerSecond);
        byte[] latest = null;
        while (_decoded.TryPeek(out var frame) && frame.Index <= due)
        {
            _decoded.TryDequeue(out frame);
            _room.Release();
            latest = frame.Pixels;
            _shown = frame.Index;
        }

        if (latest is not null)
        {
            _texture.SetData(latest);
        }

        // A clock far ahead of the decoder (a stalled window or a slow machine) restarts from the frame on screen, so the
        // video slows down instead of skipping everything it missed.
        if (due - _shown > _framesPerSecond && _shown >= 0)
        {
            _seconds = (double)_shown / _framesPerSecond;
        }
    }

    // Fills the viewport without stretching, cropping the video's overflow.
    public void Draw(SpriteBatch batch, Viewport viewport)
    {
        if (!HasFrame)
        {
            return;
        }

        var scale = Math.Max((float)viewport.Width / _texture.Width, (float)viewport.Height / _texture.Height);
        var width = (int)MathF.Ceiling(_texture.Width * scale);
        var height = (int)MathF.Ceiling(_texture.Height * scale);
        var destination = new Rectangle((viewport.Width - width) / 2, (viewport.Height - height) / 2, width, height);
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
        batch.Draw(_texture, destination, Color.White);
        batch.End();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _texture.Dispose();
        // The decoder ends at its next wait or frame; it owns the reader, and the semaphore and token stay for it.
        _ = _decoder.ContinueWith(_ =>
        {
            _stop.Dispose();
            _room.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
}

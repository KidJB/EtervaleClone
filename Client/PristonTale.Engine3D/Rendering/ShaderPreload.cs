using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering;

// Loads every shader in the content's Effects folder into the content manager's cache, one per Step, so the first
// PristonRenderer built on that content (character select) clones cached shaders instead of loading about twenty in one
// frame. Loading must stay on the graphics thread; a shader that fails is left for the renderer to report.
public sealed class ShaderPreload
{
    private const string Folder = "Effects";

    private readonly ContentManager _content;
    private readonly IClientLog _log;
    private readonly string[] _names;
    private int _next;

    public ShaderPreload(ContentManager content, IClientLog log)
    {
        _content = content;
        _log = log;
        var root = Path.IsPathRooted(content.RootDirectory) ? content.RootDirectory : Path.Combine(AppContext.BaseDirectory, content.RootDirectory);
        var folder = Path.Combine(root, Folder);
        _names = Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*.xnb").Select(static file => Folder + "/" + Path.GetFileNameWithoutExtension(file)).Order(StringComparer.Ordinal)]
            : [];
    }

    public bool IsDone => _next >= _names.Length;

    public int Count => _names.Length;

    public void Step()
    {
        if (IsDone)
        {
            return;
        }

        var name = _names[_next++];
        try
        {
            _content.Load<Effect>(name);
        }
        catch (ContentLoadException exception)
        {
            _log.Warn($"[Shaders] preload of '{name}' failed: {exception.Message}");
        }
    }
}

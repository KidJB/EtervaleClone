using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;

namespace PristonTale.Tests;

// The login screen: its background video file, the recent accounts it lists and the places of its controls.
internal static class LoginScreenTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("login video: packed frames read back in order, start over after the last, and a foreign or mixed-size file is refused", VideoAsync);
        yield return ("login: the last account is listed first once, the list keeps eight, and valid settings are kept as they are", RecentAsync);
        yield return ("login: each recent account keeps the character it last played, shown as name, level and class", LastCharacterAsync);
        yield return ("login layout: every control lies inside the design area, the recent rows fit their panel and nothing overlaps", LayoutAsync);
    }

    private static Task VideoAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "claude", "login-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var red = Path.Combine(folder, "f1.tga");
            var blue = Path.Combine(folder, "f2.tga");
            File.WriteAllBytes(red, Tga(2, 1, 255, 0, 0));
            File.WriteAllBytes(blue, Tga(2, 1, 0, 0, 255));
            using var packed = new MemoryStream();
            var header = FrameVideoReader.Pack([red, blue], 25, packed);
            Assert.True(header == new FrameVideoHeader(2, 1, 25, 2), $"header {header}");

            packed.Position = 0;
            using var reader = new FrameVideoReader(packed);
            var first = reader.NextFrame();
            var second = reader.NextFrame();
            var again = reader.NextFrame();
            Assert.True(first.Length == 8 && first[0] == 255 && first[2] == 0 && first[3] == 255, "the first frame did not decode to red RGBA");
            Assert.True(second[0] == 0 && second[2] == 255, "the second frame did not decode to blue");
            Assert.True(again.AsSpan().SequenceEqual(first), "the video did not start over after its last frame");

            Assert.Throws<InvalidDataException>(() => new FrameVideoReader(new MemoryStream(new byte[FrameVideoHeader.Size])), "a file without the video signature was read");
            var wide = Path.Combine(folder, "f3.tga");
            File.WriteAllBytes(wide, Tga(3, 1, 0, 255, 0));
            Assert.Throws<InvalidDataException>(() => FrameVideoReader.Pack([red, wide], 25, new MemoryStream()), "frames of two sizes were packed");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task RecentAsync()
    {
        var settings = new ClientSettings();
        for (var index = 0; index < ClientSettings.RecentAccountCount + 2; index++)
        {
            settings = settings.WithRecentAccount("user" + index);
        }

        settings = settings.WithRecentAccount("USER3");
        Assert.True(settings.RecentAccounts.Length == ClientSettings.RecentAccountCount, $"kept {settings.RecentAccounts.Length} accounts");
        Assert.True(settings.RecentAccounts[0] == "USER3" && settings.RecentAccounts.Count(static name => name.Equals("user3", StringComparison.OrdinalIgnoreCase)) == 1,
            "the last account is not listed first exactly once");
        Assert.True(settings.RecentAccounts[1] == "user9" && !settings.RecentAccounts.Contains("user0"), "the oldest account was not the one dropped");

        Assert.True(settings.Validate(ConsoleClientLog.Instance).RecentAccounts.SequenceEqual(settings.RecentAccounts), "valid accounts were replaced");
        var invalid = settings with { RecentAccounts = ["", "ok", "OK", new string('a', ClientSettings.AccountNameLength + 1)] };
        Assert.True(invalid.Validate(ConsoleClientLog.Instance).RecentAccounts is ["ok"], "blank, repeated or overlong accounts survived validation");
        return Task.CompletedTask;
    }

    private static Task LastCharacterAsync()
    {
        var settings = new ClientSettings()
            .WithLastCharacter(new RecentCharacter("ann", "Hero", 42, "Fighter"))
            .WithLastCharacter(new RecentCharacter("bob", "Mage", 7, "Magician"))
            .WithLastCharacter(new RecentCharacter("ANN", "Archer", 12, "Archer"));
        Assert.True(settings.RecentCharacters.Length == 2 && settings.LastCharacter("Ann") is { Name: "Archer", Level: 12 }, "an account kept two characters");
        Assert.True(settings.LastCharacter("bob")?.Name == "Mage" && settings.LastCharacter("cid") is null, "a character is listed under the wrong account");
        Assert.True(Gameplay.Screens.Opening.OpeningLoginController.Detail(settings.LastCharacter("bob")) == "Mage - Lv.7 Magician"
            && Gameplay.Screens.Opening.OpeningLoginController.Detail(null) == "", "the row's character line is wrong");

        var invalid = settings with { RecentCharacters = [.. settings.RecentCharacters, new RecentCharacter("", "Nobody", 1, "Fighter"), new RecentCharacter("dan", "Zero", 0, "Fighter")] };
        Assert.True(invalid.Validate(ConsoleClientLog.Instance).RecentCharacters.Length == 2, "a character without an account or level survived validation");
        return Task.CompletedTask;
    }

    private static Task LayoutAsync()
    {
        foreach (var (width, height) in new[] { (800, 600), (1280, 720), (1920, 1080), (3440, 1440) })
        {
            var layout = LoginLayout.Create(width, height);
            var controls = new[] { layout.Settings, layout.Recent, layout.Tab, layout.Logo, layout.AccountField, layout.PasswordField, layout.Login, layout.Exit };
            foreach (var control in controls)
            {
                Assert.True(layout.Panel.Contains(control), $"{control} leaves the design area at {width}x{height}");
            }

            Assert.True(layout.Recent.Contains(layout.RecentRow(LoginLayout.RecentRows - 1)), "the last recent row leaves its panel");
            Assert.True(!layout.AccountField.Intersects(layout.Login) && layout.PasswordField.Bottom <= layout.Login.Bottom && !layout.Recent.Intersects(layout.AccountField),
                "login controls overlap");
            Assert.True(layout.AccountField.Contains(layout.Account) && layout.PasswordField.Contains(layout.Password), "a field's text leaves its box");
        }

        return Task.CompletedTask;
    }

    // An uncompressed 24-bit TGA of one colour, stored bottom-up in BGR order.
    private static byte[] Tga(int width, int height, byte red, byte green, byte blue)
    {
        var bytes = new byte[18 + width * height * 3];
        bytes[2] = 2;
        bytes[12] = (byte)width;
        bytes[14] = (byte)height;
        bytes[16] = 24;
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            bytes[18 + pixel * 3] = blue;
            bytes[19 + pixel * 3] = green;
            bytes[20 + pixel * 3] = red;
        }

        return bytes;
    }
}

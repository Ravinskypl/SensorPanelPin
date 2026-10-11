using SensorPanelGlue.Diagnostics;
using SensorPanelGlue.Tests.TestSupport;

namespace SensorPanelGlue.Tests.Diagnostics;

public sealed class SafeDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SensorPanelGlue.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose() => TestLinks.DeleteTree(_root);

    [Fact]
    public void A_plain_file_in_a_plain_directory_opens_normally()
    {
        var directory = SafeDirectory.Open(Path.Combine(_root, "data"));

        using (var stream = directory.OpenFile("config.json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.WriteByte(42);
        }

        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(directory.Combine("config.json")));
    }

    [Fact]
    public void A_junction_in_place_of_the_directory_is_refused()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var junction = Path.Combine(_root, "data");
        Assert.True(TestLinks.CreateJunction(junction, target));

        Assert.Throws<UnsafePathException>(() => SafeDirectory.Open(junction));
    }

    [Fact]
    public void A_junction_one_level_up_is_refused_when_that_level_is_ours()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "logs"));
        var app = Path.Combine(_root, "app");
        Assert.True(TestLinks.CreateJunction(app, target));

        Assert.Throws<UnsafePathException>(() => SafeDirectory.Open(Path.Combine(app, "logs"), levels: 2));
    }

    [Fact]
    public void A_hardlinked_file_is_refused_and_its_target_is_left_untouched()
    {
        var directory = SafeDirectory.Open(Path.Combine(_root, "data"));
        var victim = Path.Combine(_root, "victim.txt");
        File.WriteAllText(victim, "PRIVILEGED");
        Assert.True(TestLinks.CreateHardLink(directory.Combine("config.json"), victim));

        Assert.Throws<UnsafePathException>(() =>
            directory.OpenFile("config.json", FileMode.Append, FileAccess.Write, FileShare.Read));

        Assert.Equal("PRIVILEGED", File.ReadAllText(victim));
    }

    [Fact]
    public void A_path_instead_of_a_file_name_is_rejected()
    {
        var directory = SafeDirectory.Open(Path.Combine(_root, "data"));

        Assert.Throws<ArgumentException>(() =>
            directory.OpenFile(@"..\escape.txt", FileMode.Create, FileAccess.Write, FileShare.None));
    }
}

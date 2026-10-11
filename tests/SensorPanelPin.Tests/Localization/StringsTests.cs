using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using SensorPanelPin.Localization;

namespace SensorPanelPin.Tests.Localization;

/// <summary>
/// The interface is English by default and Polish when Windows is in Polish. These tests keep the two
/// languages and the typed <see cref="Strings"/> class in step, so a text cannot silently go missing
/// or lose a placeholder in one language.
/// </summary>
public sealed class StringsTests
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    private static ResourceSet English => Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;

    private static ResourceSet PolishOnly => Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("pl"), createIfNotExists: true, tryParents: false)!;

    public static TheoryData<string> Members() => new(MemberNames());

    private static IEnumerable<string> MemberNames() =>
        typeof(Strings).GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m is PropertyInfo || (m is MethodInfo method && !method.IsSpecialName && method.DeclaringType == typeof(Strings)))
            .Select(m => m.Name);

    [Theory]
    [MemberData(nameof(Members))]
    public void Every_member_has_an_English_and_a_Polish_text(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(English.GetString(key)), $"No English text for {key}.");
        Assert.False(string.IsNullOrWhiteSpace(PolishOnly.GetString(key)), $"No Polish text for {key}.");
    }

    [Fact]
    public void Both_languages_have_exactly_the_same_keys()
    {
        var english = Keys(English);
        var polish = Keys(PolishOnly);

        Assert.Empty(english.Except(polish));
        Assert.Empty(polish.Except(english));
    }

    [Fact]
    public void Every_key_has_a_member_in_the_typed_class()
    {
        var members = MemberNames().ToHashSet();

        Assert.DoesNotContain(Keys(English), key => !members.Contains(key));
    }

    [Fact]
    public void Both_languages_use_the_same_placeholders()
    {
        foreach (var key in Keys(English))
        {
            Assert.True(
                Placeholders(English.GetString(key)!).SetEquals(Placeholders(PolishOnly.GetString(key)!)),
                $"Placeholders differ for {key}.");
        }
    }

    [Fact]
    public void Formatted_members_take_as_many_arguments_as_their_text_has_placeholders()
    {
        foreach (var method in typeof(Strings).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => !m.IsSpecialName))
        {
            var placeholders = Placeholders(English.GetString(method.Name)!);
            Assert.Equal(placeholders.Count, method.GetParameters().Length);
        }
    }

    [Fact]
    public void The_language_follows_the_Windows_display_language()
    {
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = Polish;
            Assert.Equal("Ekrany", Strings.ScreensTitle);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("Screens", Strings.ScreensTitle);
        }
        finally
        {
            CultureInfo.CurrentUICulture = saved;
        }
    }

    [Fact]
    public void No_Polish_text_is_left_in_the_code()
    {
        // Every Polish word belongs in Strings.pl.resx. Polish letters anywhere else in the program
        // mean a text was missed, and an English user would see it.
        var source = Path.Combine(RepositoryRoot(), "src", "SensorPanelPin");
        var offenders = Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => Regex.IsMatch(entry.line, "[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]"))
            .Select(entry => $"{Path.GetFileName(entry.path)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        Assert.Empty(offenders);
    }

    private static HashSet<string> Keys(ResourceSet set) =>
        set.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet();

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)\}").Select(match => match.Groups[1].Value).ToHashSet();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "SensorPanelPin")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root not found above " + AppContext.BaseDirectory);
    }
}

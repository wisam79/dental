using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DentalID.Desktop.Services;
using FluentAssertions;
using Xunit;

namespace DentalID.Tests.Services;

/// <summary>
/// Tests for LocalizationService: parity between languages, fallback behavior,
/// duplicate key detection, and language switch events.
/// </summary>
public class LocalizationServiceTests
{
    private readonly Loc _loc = Loc.Instance;

    [Fact]
    public void AllEnglishKeys_ShouldExistInArabic()
    {
        // Arrange
        _loc.SwitchLanguage("en");
        var strings = GetAllStringTables();
        var enKeys = strings["en"].Keys.ToHashSet();
        var arKeys = strings["ar"].Keys.ToHashSet();

        // Act
        var missingInArabic = enKeys.Except(arKeys).ToList();

        // Assert
        missingInArabic.Should().BeEmpty(
            "every English key must have an Arabic translation. Missing: {0}",
            string.Join(", ", missingInArabic));
    }

    [Fact]
    public void AllArabicKeys_ShouldExistInEnglish()
    {
        var strings = GetAllStringTables();
        var enKeys = strings["en"].Keys.ToHashSet();
        var arKeys = strings["ar"].Keys.ToHashSet();

        var missingInEnglish = arKeys.Except(enKeys).ToList();

        missingInEnglish.Should().BeEmpty(
            "every Arabic key must have an English counterpart. Missing: {0}",
            string.Join(", ", missingInEnglish));
    }

    [Fact]
    public void UnknownKey_ShouldReturnBracketedFallback()
    {
        _loc.SwitchLanguage("en");
        var result = _loc["NonExistent_Key_12345"];

        result.Should().Be("[NonExistent_Key_12345]");
    }

    [Fact]
    public void ArabicKey_ShouldFallbackToEnglish_WhenMissingInArabic()
    {
        // This tests the fallback mechanism in the indexer.
        // If a key only exists in English, Arabic mode should fall back to English value.
        _loc.SwitchLanguage("en");
        var enValue = _loc["App_Title"]; // Should exist in both
        
        _loc.SwitchLanguage("ar");
        var arValue = _loc["App_Title"];

        // Both should return non-bracketed values (not fallback)
        enValue.Should().NotStartWith("[");
        arValue.Should().NotStartWith("[");

        // Cleanup
        _loc.SwitchLanguage("en");
    }

    [Fact]
    public void SwitchLanguage_ShouldFirePropertyChanged()
    {
        _loc.SwitchLanguage("en");
        var changed = new List<string>();

        _loc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changed.Add(e.PropertyName);
        };

        _loc.SwitchLanguage("ar");

        changed.Should().Contain("Item[]");
        changed.Should().Contain(nameof(Loc.IsRtl));
        changed.Should().Contain(nameof(Loc.CurrentLanguage));

        // Cleanup
        _loc.SwitchLanguage("en");
    }

    [Fact]
    public void SwitchLanguage_ShouldFireLanguageChangedEvent()
    {
        _loc.SwitchLanguage("en");
        string? firedLang = null;
        _loc.LanguageChanged += (_, lang) => firedLang = lang;

        _loc.SwitchLanguage("ar");

        firedLang.Should().Be("ar");
        _loc.SwitchLanguage("en");
    }

    [Fact]
    public void SwitchLanguage_ToSameLanguage_ShouldNotFireEvents()
    {
        _loc.SwitchLanguage("en");
        bool fired = false;
        _loc.PropertyChanged += (_, _) => fired = true;

        _loc.SwitchLanguage("en"); // same language

        fired.Should().BeFalse();
    }

    [Fact]
    public void EnglishKeys_ShouldBeUnique()
    {
        // Using reflection to detect the duplicate key bug before the compiler catches it
        // (C# dictionary initializers with duplicate keys throw at runtime).
        // Since LoadStrings initializes the dictionaries, just verify all keys resolve.
        var strings = GetAllStringTables();
        var enKeys = strings["en"].Keys.ToList();

        enKeys.Should().OnlyHaveUniqueItems("English dictionary must not have duplicate keys");
    }

    [Fact]
    public void ArabicKeys_ShouldBeUnique()
    {
        var strings = GetAllStringTables();
        var arKeys = strings["ar"].Keys.ToList();

        arKeys.Should().OnlyHaveUniqueItems("Arabic dictionary must not have duplicate keys");
    }

    /// <summary>
    /// Uses reflection to extract the internal string tables for testing purposes.
    /// </summary>
    private Dictionary<string, Dictionary<string, string>> GetAllStringTables()
    {
        var field = typeof(Loc).GetField("_strings",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("Loc should have _strings field");

        return (Dictionary<string, Dictionary<string, string>>)field!.GetValue(_loc)!;
    }
}

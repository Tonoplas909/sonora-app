using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class ShortcutsTests
{
    [Fact]
    public void Gesture_FallsBackToTheDefault()
    {
        var s = new Shortcuts();
        Assert.Equal("Ctrl+Q", s.Gesture("queue"));
        Assert.Equal("", s.Gesture("noSuchAction"));
    }

    [Theory]
    [InlineData("ctrl + q", "Ctrl+Q")]
    [InlineData("CTRL+Q", "Ctrl+Q")]
    [InlineData("shift+ctrl+alt+f", "Ctrl+Alt+Shift+F")]
    [InlineData("F11", "F11")]
    [InlineData("  ", "")]
    [InlineData("Ctrl", "")]
    public void Normalize_PutsModifiersFirstInAFixedOrder(string input, string expected) =>
        Assert.Equal(expected, Shortcuts.Normalize(input));

    [Fact]
    public void Build_MatchesWhatNormalizeProduces()
    {
        Assert.Equal("Ctrl+Q", Shortcuts.Build(ctrl: true, alt: false, shift: false, "Q"));
        Assert.Equal("Ctrl+Alt+Shift+F", Shortcuts.Build(true, true, true, "f"));
        Assert.Equal("", Shortcuts.Build(true, false, false, ""));
    }

    [Fact]
    public void ActionFor_FindsDefaultsAndCustomGesturesAlike()
    {
        var s = new Shortcuts();
        Assert.Equal("queue", s.ActionFor("Ctrl+Q"));
        Assert.Equal("queue", s.ActionFor("ctrl+q"));
        s.Bind("queue", "Ctrl+Shift+Q");
        Assert.Equal("queue", s.ActionFor("Ctrl+Shift+Q"));
        Assert.Null(s.ActionFor("Ctrl+Q"));
        Assert.Null(s.ActionFor(""));
    }

    [Fact]
    public void Bind_TakesTheGestureFromWhoeverHeldIt()
    {
        var s = new Shortcuts();
        s.Bind("search", "Ctrl+Q"); // the queue had it
        Assert.Equal("search", s.ActionFor("Ctrl+Q"));
        Assert.True(s.IsUnbound("queue"));
        Assert.Null(s.ActionFor(s.Gesture("queue")));
    }

    [Fact]
    public void Bind_WithNothingUnbindsBackToTheDefault()
    {
        var s = new Shortcuts();
        s.Bind("queue", "Ctrl+Shift+Q");
        s.Bind("queue", "");
        Assert.Equal("Ctrl+Q", s.Gesture("queue"));
        Assert.False(s.IsUnbound("queue"));
    }

    [Fact]
    public void ResetAll_PutsEveryActionBack()
    {
        var s = new Shortcuts();
        s.Bind("queue", "Ctrl+Shift+Q");
        s.Bind("search", "Ctrl+Shift+L");
        s.ResetAll();
        Assert.All(Shortcuts.Actions, a => Assert.Equal(a.Default, s.Gesture(a.Id)));
    }

    [Fact]
    public void Repair_DropsUnknownActionsAndNormalisesTheRest()
    {
        var s = new Shortcuts { Bindings = { ["queue"] = "ctrl + shift + q", ["goneAction"] = "Ctrl+Z" } };
        s.Repair();
        Assert.Equal("Ctrl+Shift+Q", s.Gesture("queue"));
        Assert.DoesNotContain("goneAction", s.Bindings.Keys);
    }

    [Fact]
    public void Defaults_AreAllDistinctAndNormalised()
    {
        var gestures = Shortcuts.Actions.Select(a => a.Default).ToList();
        Assert.Equal(gestures.Count, gestures.Distinct().Count());
        Assert.All(gestures, g => Assert.Equal(g, Shortcuts.Normalize(g)));
    }
}

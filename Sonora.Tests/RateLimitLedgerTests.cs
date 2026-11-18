using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class RateLimitLedgerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "sonora-ledger-" + Guid.NewGuid().ToString("N"));
    string File => Path.Combine(_dir, "limit.json");
    static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void ABlockIsKeptUntilItsResetTimeAndNoLonger()
    {
        var ledger = new RateLimitLedger(null, Now);
        ledger.Block("me", Now.AddHours(2), Now);
        Assert.Equal(Now.AddHours(2), ledger.BlockedUntil("me", Now.AddHours(1)));
        Assert.Null(ledger.BlockedUntil("me", Now.AddHours(2)));
        Assert.Null(ledger.BlockedUntil("me/tracks", Now));
    }

    [Fact]
    public void ABlockSurvivesARestart()
    {
        new RateLimitLedger(File, Now).Block("me/tracks", Now.AddMinutes(90), Now);

        var reopened = new RateLimitLedger(File, Now.AddMinutes(10));
        Assert.Equal(Now.AddMinutes(90), reopened.BlockedUntil("me/tracks", Now.AddMinutes(10)));
    }

    [Fact]
    public void ABlockThatIsOverIsNotRestoredAndTheFileGoesAway()
    {
        var ledger = new RateLimitLedger(File, Now);
        ledger.Block("me", Now.AddMinutes(5), Now);

        Assert.Null(new RateLimitLedger(File, Now.AddMinutes(6)).BlockedUntil("me", Now.AddMinutes(6)));
        ledger.Block("other", Now.AddMinutes(1), Now.AddMinutes(6)); // writing again drops what is over
        Assert.False(System.IO.File.Exists(File));
    }

    [Fact]
    public void ALaterBlockWinsAndAnEarlierOneNeverShortensIt()
    {
        var ledger = new RateLimitLedger(null, Now);
        ledger.Block("me", Now.AddHours(3), Now);
        ledger.Block("me", Now.AddMinutes(1), Now);
        Assert.Equal(Now.AddHours(3), ledger.BlockedUntil("me", Now));
    }

    [Fact]
    public void LastOpeningIsTheLatestBlockOrNothing()
    {
        var ledger = new RateLimitLedger(null, Now);
        Assert.Null(ledger.LastOpening(Now));
        ledger.Block("a", Now.AddMinutes(10), Now);
        ledger.Block("b", Now.AddHours(2), Now);
        Assert.Equal(Now.AddHours(2), ledger.LastOpening(Now));
        Assert.Null(ledger.LastOpening(Now.AddHours(3)));
    }

    [Fact]
    public void ABrokenFileIsIgnored()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "{ not json");
        var ledger = new RateLimitLedger(File, Now);
        Assert.Null(ledger.BlockedUntil("me", Now));
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        var ledger = new RateLimitLedger(File, Now);
        ledger.Block("me", DateTime.UtcNow.AddHours(1));
        ledger.Clear();
        Assert.Null(ledger.LastOpening());
        Assert.Null(new RateLimitLedger(File).LastOpening());
    }
}

using Sonarr.Bot.Observability;

namespace Sonarr.Application.Tests.Web;

/// <summary>
/// The store behind the panel's "My errors" page (docs/09). In-memory and bounded, so the things
/// worth pinning down are the bounds and the ordering.
/// </summary>
public class UserErrorLogTests
{
    [Fact]
    public void Newest_first()
    {
        UserErrorLog log = new();

        log.Record(7, Error("a"));
        log.Record(7, Error("b"));

        // The page shows what just broke, not what broke first.
        Assert.Equal(["b", "a"], log.Recent(7).Select(e => e.CaseId));
    }

    [Fact]
    public void Keeps_only_the_last_ten_per_user()
    {
        UserErrorLog log = new();

        for (var i = 0; i < UserErrorLog.PerUser + 5; i++)
        {
            log.Record(7, Error(i.ToString()));
        }

        IReadOnlyList<UserError> recent = log.Recent(7);

        Assert.Equal(UserErrorLog.PerUser, recent.Count);
        Assert.Equal("14", recent[0].CaseId);
        Assert.DoesNotContain(recent, e => e.CaseId == "4");
    }

    [Fact]
    public void One_users_errors_are_not_anothers()
    {
        UserErrorLog log = new();

        log.Record(7, Error("mine"));

        Assert.Empty(log.Recent(8));
    }

    [Fact]
    public void Stops_tracking_new_users_at_the_cap()
    {
        // The bound that matters operationally: a guild-wide outage must not turn this into an
        // unbounded dictionary keyed by every member who ran a command.
        UserErrorLog log = new();

        for (ulong user = 1; user <= (ulong)UserErrorLog.MaxUsers; user++)
        {
            log.Record(user, Error("x"));
        }

        log.Record(99_999, Error("late"));
        Assert.Empty(log.Recent(99_999));

        // Someone already being tracked still gets their errors recorded.
        log.Record(1, Error("still-here"));
        Assert.Equal("still-here", log.Recent(1)[0].CaseId);
    }

    [Fact]
    public void Ignores_a_missing_user_id()
    {
        // InteractionHandler passes 0 when the interaction has no user — better a dropped row than
        // a shared bucket every unauthenticated failure lands in.
        UserErrorLog log = new();

        log.Record(0, Error("nobody"));

        Assert.Empty(log.Recent(0));
    }

    private static UserError Error(string caseId) =>
        new(caseId, "/test", "Something broke on my end.", DateTimeOffset.UnixEpoch);
}

using Microsoft.Extensions.DependencyInjection;
using Sonarr.Application.Tests.Levels;
using Sonarr.Application.Web;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Entities.Web;
using Sonarr.Domain.Web;

namespace Sonarr.Application.Tests.Web;

/// <summary>
/// The login flow through in-memory fakes (docs/09). What is asserted here is the security
/// behaviour: no enumeration, single-use codes, a per-user attempt cap, and revocation that bites
/// immediately on sensitive routes.
/// </summary>
public class WebAuthServiceTests
{
    private const long UserId = 4242;
    private const long GuildId = 7;
    private const string Handle = "someone";

    [Fact]
    public async Task Request_issues_a_token_for_a_known_handle()
    {
        Harness h = Harness.Build();

        LoginRequest request = await h.Auth.RequestTokenAsync($"@{Handle}", "10.0.0.1");

        Assert.Equal(LoginRequestOutcome.Issued, request.Outcome);
        Assert.Equal((ulong)UserId, request.UserId);
        Assert.NotNull(request.Token);

        // Hashed at rest: the stored row must not be reversible to the code we DM'd.
        Assert.Equal(1, h.Repo.TokenCount);
        LoginToken row = h.Repo.Token(WebAuthRules.Hash(request.Token!));
        Assert.Equal(UserId, row.UserId);
        Assert.False(row.Used);
    }

    [Fact]
    public async Task Request_for_an_unknown_handle_reveals_nothing_and_stores_nothing()
    {
        Harness h = Harness.Build();

        LoginRequest request = await h.Auth.RequestTokenAsync("nobody-here", "10.0.0.1");

        Assert.Equal(LoginRequestOutcome.UnknownUser, request.Outcome);
        Assert.Null(request.Token);
        Assert.Equal(0, h.Repo.TokenCount);
    }

    [Fact]
    public async Task Request_costs_quota_even_when_the_handle_is_unknown()
    {
        // The point of the ordering: probing handles must not be free, or the rate limit only
        // protects accounts that exist. Both windows are spent before the lookup happens.
        Harness h = Harness.Build();

        LoginRequest request = await h.Auth.RequestTokenAsync("nobody-here", "10.0.0.1");

        Assert.Equal(LoginRequestOutcome.UnknownUser, request.Outcome);
        Assert.Equal(["ip:10.0.0.1", "user:nobody-here"], h.Cooldowns.LoginAttempts);
    }

    [Fact]
    public async Task Request_rejects_a_handle_that_is_not_a_handle()
    {
        Harness h = Harness.Build();

        Assert.Equal(
            LoginRequestOutcome.InvalidUsername,
            (await h.Auth.RequestTokenAsync("   ", "10.0.0.1")).Outcome);
        Assert.Empty(h.Cooldowns.LoginAttempts);
    }

    [Fact]
    public async Task Verify_mints_a_session_and_mirrors_it()
    {
        Harness h = Harness.Build();
        LoginRequest request = await h.Auth.RequestTokenAsync(Handle, "10.0.0.1");

        LoginResult result = await h.Auth.VerifyAsync(Handle, request.Token, remember: false, "agent/1");

        Assert.True(result.Success);
        Assert.NotNull(result.RawSessionId);
        Assert.Equal((ulong)UserId, result.UserId);
        Assert.True(h.Cache.Mirrors(WebAuthRules.Hash(result.RawSessionId!)));

        // 24 h for a plain login; the remember-me window is four times a week away.
        Assert.False(WebAuthRules.IsRemembered(DateTimeOffset.UtcNow, result.ExpiresAt));
    }

    [Fact]
    public async Task Verify_accepts_the_code_the_way_a_human_retypes_it()
    {
        Harness h = Harness.Build();
        LoginRequest request = await h.Auth.RequestTokenAsync(Handle, "10.0.0.1");

        LoginResult result = await h.Auth.VerifyAsync(
            $"@{Handle}", WebAuthRules.Format(request.Token!).ToLowerInvariant(), remember: true, null);

        Assert.True(result.Success);
        Assert.True(WebAuthRules.IsRemembered(DateTimeOffset.UtcNow, result.ExpiresAt));
    }

    [Fact]
    public async Task Verify_burns_the_code()
    {
        Harness h = Harness.Build();
        LoginRequest request = await h.Auth.RequestTokenAsync(Handle, "10.0.0.1");

        Assert.True((await h.Auth.VerifyAsync(Handle, request.Token, false, null)).Success);

        LoginResult second = await h.Auth.VerifyAsync(Handle, request.Token, false, null);
        Assert.Equal(LoginOutcome.Expired, second.Outcome);
    }

    [Fact]
    public async Task Verify_rejects_a_code_issued_to_someone_else()
    {
        Harness h = Harness.Build();
        h.Members.Named(GuildId, 999, "other");

        LoginRequest mine = await h.Auth.RequestTokenAsync(Handle, "10.0.0.1");

        LoginResult stolen = await h.Auth.VerifyAsync("other", mine.Token, false, null);

        Assert.Equal(LoginOutcome.InvalidCode, stolen.Outcome);
        Assert.Null(stolen.RawSessionId);
    }

    [Fact]
    public async Task Verify_rejects_an_expired_code()
    {
        Harness h = Harness.Build();
        LoginRequest request = await h.Auth.RequestTokenAsync(Handle, "10.0.0.1");
        h.Repo.Token(WebAuthRules.Hash(request.Token!)).ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        Assert.Equal(
            LoginOutcome.Expired,
            (await h.Auth.VerifyAsync(Handle, request.Token, false, null)).Outcome);
    }

    [Fact]
    public async Task Exhausted_attempts_kill_the_token()
    {
        Harness h = Harness.Build();
        await h.Auth.RequestTokenAsync(Handle, "10.0.0.1");
        h.Cooldowns.AllowVerify = false;

        LoginResult result = await h.Auth.VerifyAsync(Handle, "ABCD2345", false, null);

        Assert.Equal(LoginOutcome.TooManyAttempts, result.Outcome);

        // Not just "wrong code": the outstanding token is gone, so the real code is useless too.
        Assert.Equal(0, h.Repo.TokenCount);
    }

    [Fact]
    public async Task Attempt_cap_is_keyed_per_user_not_per_code()
    {
        Harness h = Harness.Build();
        await h.Auth.RequestTokenAsync(Handle, "10.0.0.1");

        await h.Auth.VerifyAsync(Handle, "AAAA2345", false, null);
        await h.Auth.VerifyAsync(Handle, "BBBB2345", false, null);

        // Two different guesses, two spent attempts against the same key — walking the keyspace
        // does not buy an attacker more tries.
        Assert.Equal([$"user:{UserId}", $"user:{UserId}"], h.Cooldowns.VerifyAttempts);
    }

    [Fact]
    public async Task Authenticate_reads_the_mirror_for_ordinary_routes()
    {
        Harness h = Harness.Build();
        string session = await h.LoginAsync();

        PanelUser? user = await h.Auth.AuthenticateAsync(session, requireFresh: false);

        Assert.NotNull(user);
        Assert.Equal((ulong)UserId, user!.UserId);
        Assert.False(user.IsAdmin);
    }

    [Fact]
    public async Task Authenticate_falls_back_to_the_row_when_the_cache_is_blind()
    {
        Harness h = Harness.Build();
        string session = await h.LoginAsync();
        h.Cache.Blind = true;

        // Fails open: a Redis outage costs a DB read, not every login.
        Assert.NotNull(await h.Auth.AuthenticateAsync(session, requireFresh: false));
    }

    [Fact]
    public async Task Revoked_session_still_passes_a_cached_read_but_not_a_fresh_one()
    {
        Harness h = Harness.Build();
        string session = await h.LoginAsync();
        string hash = WebAuthRules.Hash(session);

        // Revoke the row behind the service's back — an admin revocation, or another node's
        // logout-all that has not dropped this mirror yet.
        h.Repo.Session(hash).Revoked = true;

        Assert.NotNull(await h.Auth.AuthenticateAsync(session, requireFresh: false));
        Assert.Null(await h.Auth.AuthenticateAsync(session, requireFresh: true));

        // And the fresh read cleaned up the stale mirror, so the cached path stops lying too.
        Assert.Null(await h.Auth.AuthenticateAsync(session, requireFresh: false));
    }

    [Fact]
    public async Task Authenticate_renews_a_session_past_its_half_life()
    {
        Harness h = Harness.Build();
        string session = await h.LoginAsync();
        string hash = WebAuthRules.Hash(session);

        WebSession row = h.Repo.Session(hash);
        row.CreatedAt = DateTimeOffset.UtcNow.AddHours(-20);
        row.ExpiresAt = DateTimeOffset.UtcNow.AddHours(4);
        h.Cache.Blind = true;

        PanelUser? user = await h.Auth.AuthenticateAsync(session, requireFresh: true);

        Assert.NotNull(user);
        Assert.NotNull(user!.RenewedUntil);
        Assert.True(h.Repo.Session(hash).ExpiresAt > DateTimeOffset.UtcNow.AddHours(20));
    }

    [Fact]
    public async Task Expired_session_is_rejected()
    {
        Harness h = Harness.Build();
        string session = await h.LoginAsync();
        h.Repo.Session(WebAuthRules.Hash(session)).ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        Assert.Null(await h.Auth.AuthenticateAsync(session, requireFresh: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public async Task Authenticate_rejects_junk_without_touching_storage(string? cookie)
    {
        Harness h = Harness.Build();

        Assert.Null(await h.Auth.AuthenticateAsync(cookie, requireFresh: true));
    }

    [Fact]
    public async Task Logout_drops_the_mirror_and_revokes_the_row()
    {
        Harness h = Harness.Build();
        string session = await h.LoginAsync();
        string hash = WebAuthRules.Hash(session);

        Assert.True(await h.Auth.LogoutAsync(session));

        Assert.False(h.Cache.Mirrors(hash));
        Assert.True(h.Repo.Session(hash).Revoked);
        Assert.False(await h.Auth.LogoutAsync(session));
    }

    [Fact]
    public async Task Logout_all_revokes_every_session_and_every_mirror()
    {
        Harness h = Harness.Build();
        string first = await h.LoginAsync();
        string second = await h.LoginAsync();

        Assert.Equal(2, await h.Auth.LogoutAllAsync((ulong)UserId));

        Assert.False(h.Cache.Mirrors(WebAuthRules.Hash(first)));
        Assert.False(h.Cache.Mirrors(WebAuthRules.Hash(second)));
        Assert.Null(await h.Auth.AuthenticateAsync(second, requireFresh: true));
    }

    [Fact]
    public async Task Admin_status_comes_from_the_allow_list_per_request()
    {
        Harness h = Harness.Build(admins: [UserId]);
        string session = await h.LoginAsync();

        PanelUser? user = await h.Auth.AuthenticateAsync(session, requireFresh: true);

        Assert.True(user!.IsAdmin);
    }

    [Fact]
    public async Task Audit_records_the_actor_the_action_and_the_detail()
    {
        Harness h = Harness.Build();
        string session = await h.LoginAsync();
        PanelUser user = (await h.Auth.AuthenticateAsync(session, requireFresh: true))!;

        await h.Auth.AuditAsync(user, "admin.config_set", "greeting_channel", GuildId, new { value = "1" });

        WebAudit entry = Assert.Single(h.Repo.Audits);
        Assert.Equal(UserId, entry.UserId);
        Assert.Equal("admin.config_set", entry.Action);
        Assert.Equal("greeting_channel", entry.Target);
        Assert.Equal(GuildId, entry.GuildId);
        Assert.Equal("1", entry.Detail["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Audit_truncates_rather_than_letting_the_insert_fail()
    {
        Harness h = Harness.Build();
        string session = await h.LoginAsync();
        PanelUser user = (await h.Auth.AuthenticateAsync(session, requireFresh: true))!;

        await h.Auth.AuditAsync(user, new string('a', 200), new string('b', 400));

        WebAudit entry = Assert.Single(h.Repo.Audits);
        Assert.Equal(64, entry.Action.Length);
        Assert.Equal(256, entry.Target!.Length);
    }

    private sealed class Harness
    {
        public required IWebAuthService Auth { get; init; }

        public required FakeWebAuthRepository Repo { get; init; }

        public required FakeWebSessionCache Cache { get; init; }

        public required FakeMemberRepository Members { get; init; }

        public required RecordingCooldownStore Cooldowns { get; init; }

        public static Harness Build(IEnumerable<ulong>? admins = null)
        {
            FakeWebAuthRepository repo = new();
            FakeWebSessionCache cache = new();
            FakeMemberRepository members = new FakeMemberRepository().Named(GuildId, UserId, Handle);
            RecordingCooldownStore cooldowns = new();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton<IWebAuthRepository>(repo);
            services.AddSingleton<IWebSessionCache>(cache);
            services.AddSingleton<IMemberRepository>(members);
            services.AddSingleton<ICooldownStore>(cooldowns);
            services.AddSingleton(new AdminAllowList(admins ?? []));
            services.AddSonarrWebAuth();

            return new Harness
            {
                Auth = services.BuildServiceProvider().GetRequiredService<IWebAuthService>(),
                Repo = repo,
                Cache = cache,
                Members = members,
                Cooldowns = cooldowns,
            };
        }

        /// <summary>Request + verify, returning the raw session id the cookie would carry.</summary>
        public async Task<string> LoginAsync()
        {
            LoginRequest request = await Auth.RequestTokenAsync(Handle, "10.0.0.1");
            LoginResult result = await Auth.VerifyAsync(Handle, request.Token, false, "agent/1");
            return result.RawSessionId!;
        }
    }
}

/// <summary>
/// Cooldown store that records what it was asked, so a test can assert the ordering and the keys
/// rather than only the outcome.
/// </summary>
public sealed class RecordingCooldownStore : ICooldownStore
{
    public bool AllowLogin { get; set; } = true;

    public bool AllowVerify { get; set; } = true;

    public List<string> LoginAttempts { get; } = [];

    public List<string> VerifyAttempts { get; } = [];

    public Task<bool> TryAcquireXpAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> TryAcquireCommandAsync(ulong userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> TryConsumeLoginAsync(string identifier, CancellationToken cancellationToken = default)
    {
        LoginAttempts.Add(identifier);
        return Task.FromResult(AllowLogin);
    }

    public Task<bool> TryConsumeVerifyAsync(string identifier, CancellationToken cancellationToken = default)
    {
        VerifyAttempts.Add(identifier);
        return Task.FromResult(AllowVerify);
    }

    public Task<int> RecordMessageHashAsync(
        ulong guildId, ulong userId, string hash, CancellationToken cancellationToken = default) =>
        Task.FromResult(1);

    public Task ClearMessageHashesAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

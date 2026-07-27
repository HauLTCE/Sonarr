using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Entities.Mod;
using Sonarr.Domain.Entities.Music;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Entities.Stats;
using Sonarr.Domain.Entities.Web;

namespace Sonarr.Infrastructure.Persistence;

/// <summary>
/// The one EF Core context (docs/02-architecture.md). Schema-per-area, snake_case
/// names, pgvector enabled. Npgsql's <c>UseVector()</c> is wired by the host.
/// </summary>
public class SonarrDbContext(DbContextOptions<SonarrDbContext> options) : DbContext(options)
{
    // core
    public DbSet<Guild> Guilds => Set<Guild>();
    public DbSet<GuildConfig> GuildConfigs => Set<GuildConfig>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<Job> Jobs => Set<Job>();

    // levels
    public DbSet<LevelProgress> LevelProgress => Set<LevelProgress>();
    public DbSet<LevelReward> LevelRewards => Set<LevelReward>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<SeasonResult> SeasonResults => Set<SeasonResult>();

    // mod
    public DbSet<ModCase> Cases => Set<ModCase>();
    public DbSet<InfractionSummary> InfractionSummaries => Set<InfractionSummary>();

    // music
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistTrack> PlaylistTracks => Set<PlaylistTrack>();
    public DbSet<PlayHistory> PlayHistory => Set<PlayHistory>();
    public DbSet<TrackRating> TrackRatings => Set<TrackRating>();
    public DbSet<MusicUserPrefs> MusicUserPrefs => Set<MusicUserPrefs>();

    // chat
    public DbSet<Person> People => Set<Person>();
    public DbSet<Fact> Facts => Set<Fact>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<RelationshipEvent> RelationshipEvents => Set<RelationshipEvent>();
    public DbSet<Stance> Stances => Set<Stance>();
    public DbSet<StanceAgreement> StanceAgreements => Set<StanceAgreement>();
    public DbSet<ChatGuildState> ChatGuildStates => Set<ChatGuildState>();
    public DbSet<IntentEmbedding> IntentEmbeddings => Set<IntentEmbedding>();

    // social
    public DbSet<QuoteBoard> Quotes => Set<QuoteBoard>();
    public DbSet<Capsule> Capsules => Set<Capsule>();
    public DbSet<SocialEvent> Events => Set<SocialEvent>();
    public DbSet<EventRsvp> EventRsvps => Set<EventRsvp>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    // web
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
    public DbSet<WebSession> WebSessions => Set<WebSession>();
    public DbSet<WebAudit> WebAudits => Set<WebAudit>();

    // stats
    public DbSet<CommandUsage> CommandUsage => Set<CommandUsage>();
    public DbSet<ActivitySample> ActivitySamples => Set<ActivitySample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SonarrDbContext).Assembly);
        SnakeCaseNames.Apply(modelBuilder);
    }
}

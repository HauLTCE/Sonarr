using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Infrastructure.Persistence.Configurations.Chat;

internal sealed class RelationshipEventConfiguration : IEntityTypeConfiguration<RelationshipEvent>
{
    public void Configure(EntityTypeBuilder<RelationshipEvent> builder)
    {
        builder.ToTable("relationship_event", "chat");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.Cause).HasMaxLength(128).IsRequired();
        builder.Property(e => e.Delta).IsJsonb();
        builder.HasAuditTimestamps();

        // #trend# windows and grudge decay both scan a person's recent events.
        builder.HasIndex(e => new { e.GuildId, e.UserId, e.At });
    }
}

internal sealed class ChatGuildStateConfiguration : IEntityTypeConfiguration<ChatGuildState>
{
    public void Configure(EntityTypeBuilder<ChatGuildState> builder)
    {
        builder.ToTable("guild_state", "chat");
        builder.HasKey(s => s.GuildId);
        builder.Property(s => s.GuildId).ValueGeneratedNever();
        builder.Property(s => s.RoomMood).IsJsonb();
        builder.Property(s => s.EventLog).IsJsonb();
        builder.HasAuditTimestamps();
    }
}

internal sealed class IntentEmbeddingConfiguration : IEntityTypeConfiguration<IntentEmbedding>
{
    public void Configure(EntityTypeBuilder<IntentEmbedding> builder)
    {
        builder.ToTable("intent_embedding", "chat");
        builder.HasKey(e => e.ContentHash);
        builder.Property(e => e.ContentHash).HasMaxLength(64);
        builder.Property(e => e.IntentId).HasMaxLength(128).IsRequired();
        builder.Property(e => e.Example).HasMaxLength(1024).IsRequired();
        builder.Property(e => e.Embedding)
            .HasColumnType($"vector({EpisodeConfiguration.EmbeddingDimensions})");
        builder.HasAuditTimestamps();

        // Small table, loaded wholesale into the matcher at startup: no vector index.
        builder.HasIndex(e => e.IntentId);
    }
}

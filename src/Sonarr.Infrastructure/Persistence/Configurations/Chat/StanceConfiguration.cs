using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Infrastructure.Persistence.Configurations.Chat;

internal sealed class StanceConfiguration : IEntityTypeConfiguration<Stance>
{
    public void Configure(EntityTypeBuilder<Stance> builder)
    {
        builder.ToTable("stance", "chat");
        builder.HasKey(s => s.Topic);
        builder.Property(s => s.Topic).HasMaxLength(64);
        builder.Property(s => s.StanceText).HasColumnName("stance").HasMaxLength(512).IsRequired();
        builder.Property(s => s.PoolRef).HasMaxLength(128).IsRequired();
        builder.HasAuditTimestamps();
    }
}

internal sealed class StanceAgreementConfiguration : IEntityTypeConfiguration<StanceAgreement>
{
    public void Configure(EntityTypeBuilder<StanceAgreement> builder)
    {
        builder.ToTable("stance_agreement", "chat");
        builder.HasKey(a => new { a.GuildId, a.UserId, a.Topic });
        builder.Property(a => a.Topic).HasMaxLength(64);
        builder.HasAuditTimestamps();

        builder.HasOne(a => a.Stance)
            .WithMany(s => s.Agreements)
            .HasForeignKey(a => a.Topic)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

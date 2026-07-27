namespace Sonarr.Domain.Entities.Social;

/// <summary>
/// social.quote_board — quotes saved with /quote save. The chat engine can recall these.
/// </summary>
public class QuoteBoard : AuditedEntity
{
    public long QuoteId { get; set; }

    public long GuildId { get; set; }

    /// <summary>Who said the quoted line.</summary>
    public long AuthorId { get; set; }

    /// <summary>Who pressed save.</summary>
    public long SavedBy { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Source message, for a jump link. Null if quoted manually.</summary>
    public long? MessageId { get; set; }

    public long? ChannelId { get; set; }
}

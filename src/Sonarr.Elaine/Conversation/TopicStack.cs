using System.Collections.Immutable;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// What the conversation has been about lately, most recent first, capped at
/// <see cref="Capacity"/>.
/// </summary>
/// <remarks>
/// The old engine had one topic slot, so mentioning a second subject erased the first and
/// callbacks ("you were just complaining about your job") had nothing to reach for. This
/// keeps a short history with recency weights, which the matcher turns into topic affinity.
/// <para>Weights decay per <em>turn the topic was not mentioned</em>, so a topic raised
/// once and dropped fades out instead of pinning her to it.</para>
/// </remarks>
public sealed record TopicStack
{
    /// <summary>docs/10: cap 5.</summary>
    public const int Capacity = 5;

    /// <summary>Fraction of its weight a topic keeps per turn it goes unmentioned.</summary>
    public const double DecayFactor = 0.75;

    /// <summary>Below this a topic is stale enough to forget.</summary>
    public const double ForgetBelow = 0.15;

    public static readonly TopicStack Empty = new([]);

    private readonly ImmutableList<TopicEntry> _entries;

    private TopicStack(ImmutableList<TopicEntry> entries) => _entries = entries;

    public static TopicStack Restore(IEnumerable<TopicEntry>? entries) =>
        new([.. (entries ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Topic))
            .OrderByDescending(e => e.Weight)
            .Take(Capacity)]);

    /// <summary>Strongest first.</summary>
    public IReadOnlyList<TopicEntry> Entries => _entries;

    /// <summary>The live topic the matcher grants affinity to, or null when nothing is warm.</summary>
    public string? Current => _entries.Count > 0 ? _entries[0].Topic : null;

    public double WeightOf(string topic) =>
        _entries.FirstOrDefault(e => e.Topic == topic)?.Weight ?? 0;

    /// <summary>
    /// Ages every topic one turn, then raises <paramref name="mentioned"/> back to full
    /// weight (or adds it). Passing null just ages.
    /// </summary>
    public TopicStack Advance(string? mentioned)
    {
        List<TopicEntry> next = [.. _entries
            .Select(e => e with { Weight = e.Weight * DecayFactor })
            .Where(e => e.Weight >= ForgetBelow)];

        if (!string.IsNullOrWhiteSpace(mentioned))
        {
            next.RemoveAll(e => e.Topic == mentioned);
            next.Insert(0, new TopicEntry(mentioned, 1.0));
        }

        return new TopicStack([.. next
            .OrderByDescending(e => e.Weight)
            .Take(Capacity)]);
    }
}

/// <param name="Weight">1.0 the turn it was mentioned, decaying from there.</param>
public sealed record TopicEntry(string Topic, double Weight);

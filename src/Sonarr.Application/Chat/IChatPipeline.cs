namespace Sonarr.Application.Chat;

/// <summary>
/// The chat adapter, as the gateway handler sees it: one message in, one decision out.
/// </summary>
/// <remarks>
/// An interface only so the handler can be tested against a fake. It lives here rather than in
/// <c>Sonarr.Domain.Abstractions</c> because its request and decision types are adapter shapes,
/// not domain entities.
/// </remarks>
public interface IChatPipeline
{
    Task<ChatDecision> HandleAsync(ChatRequest request, CancellationToken ct = default);
}

namespace Sonarr.Iris.Panacea;

/// <summary>
/// Lavalink, examined the way the bot's own probe does: an authenticated GET on
/// <c>/v4/info</c>. Not ours to restart in general — it is a pinned third-party service — but
/// it runs as a container here, so the stopped-container remedy applies to it too. A rejected
/// password is deliberately not treated: no remedy can guess a credential, and reporting it is
/// the fix.
/// </summary>
/// <remarks>
/// Without Lavalink she still talks; only music stops. So this is a red row, not a boot
/// blocker — and the advice says which half of the bot is affected, because "lavalink is down"
/// means nothing to somebody wondering why <c>/play</c> returns an error.
/// </remarks>
internal sealed class LavalinkCheck : IDoctorCheck
{
    /// <summary>Five seconds, the same as the bot's probe: this is a local socket.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Set by the examination: a bad password is not something treatment can help.</summary>
    private bool _rejectedCredentials;

    public string Name => "lavalink";

    public async Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        string? raw = CliHost.Configuration["LAVALINK_URI"];
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out Uri? baseUri))
        {
            return Diagnosis.Blocked("LAVALINK_URI is not a usable URI — see the env row");
        }

        _rejectedCredentials = false;
        Uri info = new(baseUri, "/v4/info");

        try
        {
            using HttpClient http = new() { Timeout = Timeout };
            using HttpRequestMessage request = new(HttpMethod.Get, info);

            // The raw password as the Authorization value, no scheme, added without validation:
            // the typed header throws FormatException on a password containing a space, and that
            // exception message would echo the password into the report.
            request.Headers.TryAddWithoutValidation(
                "Authorization", CliHost.Configuration["LAVALINK_PASSWORD"] ?? "");

            using HttpResponseMessage response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return Diagnosis.Ok($"{info.Host}:{info.Port} responding");
            }

            if ((int)response.StatusCode == 401)
            {
                _rejectedCredentials = true;
                return Diagnosis.Broken(
                    "rejected our credentials",
                    "LAVALINK_PASSWORD does not match the one in Lavalink's application.yml — "
                    + "no remedy can guess it, so fix the pair by hand");
            }

            return Diagnosis.Broken(
                $"HTTP {(int)response.StatusCode} from {info.Host}:{info.Port}",
                "Lavalink is answering but not with info — check its own log; music will not work "
                + "until it does, though the rest of her is unaffected");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Diagnosis.Broken(
                $"no answer from {info.Host}:{info.Port} — {Doctor.OneLine(ex.Message)}",
                "start the Lavalink container (the doctor tries this itself); until it is up "
                + "/play fails and the rest of her is fine");
        }
    }

    public async Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
    {
        if (_rejectedCredentials)
        {
            return ["left Lavalink alone — it is running and refusing the password, "
                + "which is a value only a person can fix"];
        }

        return await DockerRemedy.TryStartAsync("lavalink", async () =>
            (await ExamineAsync(ct)).Healthy, ct);
    }
}

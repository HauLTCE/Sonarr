import { count, when } from "@/lib/format";
import { currentLocale } from "@/lib/locale";
import { translator, type Translate } from "@/lib/strings";

type Status = {
  status: "ok" | "degraded" | "unknown";
  startedAt: string;
  uptimeSeconds: number;
  checkedAt: string | null;
  checks: { name: string; healthy: boolean; detail: string | null }[] | null;
  live: {
    at: string;
    gateway: string;
    latencyMs: number;
    guilds: number;
    players: { guildId: string; state: string; queued: number; nowPlaying: string | null }[];
  } | null;
};

/**
 * Uptime, the self-test checks and live gateway state (checklist 295).
 *
 * `/api/status` is the unauthenticated blob the status page uses, so this reads it directly rather
 * than through the session helper — there is nothing here an admin sees that a visitor could not.
 * A missing `live` object means the pusher has not written in the last five seconds, which the page
 * says rather than papering over with an old latency figure.
 */
export default async function AdminStatusPage() {
  const locale = await currentLocale();
  const t = translator(locale);

  const base = process.env.SONARR_API_URL ?? "http://127.0.0.1:5088";
  let status: Status | null = null;

  try {
    const response = await fetch(`${base}/api/status`, { cache: "no-store" });
    status = response.ok ? ((await response.json()) as Status) : null;
  } catch {
    status = null;
  }

  if (!status) {
    return (
      <>
        <h1>{t("admin.status.title")}</h1>
        <p className="notice bad" role="alert">
          {t("admin.status.down")}
        </p>
      </>
    );
  }

  return (
    <>
      <h1>{t("admin.status.title")}</h1>

      <section className="card">
        <p className={`notice ${status.status === "ok" ? "good" : status.status === "degraded" ? "bad" : "warn"}`}>
          {t(`admin.status.${status.status}`)}
        </p>

        <dl className="grid">
          <div className="stat">
            <dt>{t("admin.status.uptime")}</dt>
            <dd style={{ fontSize: "1.1rem" }}>{uptime(t, status.uptimeSeconds)}</dd>
          </div>
          <div className="stat">
            <dt>{t("admin.status.gateway")}</dt>
            <dd style={{ fontSize: "1.1rem" }}>
              {status.live?.gateway ?? t("admin.status.unknown")}
            </dd>
          </div>
          <div className="stat">
            <dt>{t("admin.status.latency")}</dt>
            <dd style={{ fontSize: "1.1rem" }}>
              {status.live ? `${count(locale, status.live.latencyMs)} ms` : "—"}
            </dd>
          </div>
          <div className="stat">
            <dt>{t("admin.status.guilds")}</dt>
            <dd style={{ fontSize: "1.1rem" }}>
              {status.live ? count(locale, status.live.guilds) : "—"}
            </dd>
          </div>
        </dl>

        <p className="hint">
          {t("admin.status.startedAt")}: {when(locale, status.startedAt)} ·{" "}
          {t("admin.status.checkedAt")}: {when(locale, status.checkedAt)}
        </p>

        {!status.live && <p className="notice warn">{t("admin.status.stale")}</p>}
      </section>

      <section className="card">
        <h2>{t("admin.status.checks")}</h2>
        {!status.checks || status.checks.length === 0 ? (
          <p className="empty">{t("admin.status.noChecks")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("admin.status.check")}</th>
                  <th scope="col">{t("admin.status.result")}</th>
                  <th scope="col">{t("admin.status.detail")}</th>
                </tr>
              </thead>
              <tbody>
                {status.checks.map((check) => (
                  <tr key={check.name}>
                    <td>{check.name}</td>
                    {/* Word, not just a colour — the row has to read the same in greyscale. */}
                    <td>{t(check.healthy ? "admin.status.healthy" : "admin.status.failing")}</td>
                    <td className="hint">{check.detail ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2>{t("admin.status.players")}</h2>
        {!status.live || status.live.players.length === 0 ? (
          <p className="empty">{t("admin.status.noPlayers")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("guild.label")}</th>
                  <th scope="col">{t("admin.status.playerState")}</th>
                  <th scope="col" className="num">
                    {t("admin.status.queued")}
                  </th>
                  <th scope="col">{t("admin.status.nowPlaying")}</th>
                </tr>
              </thead>
              <tbody>
                {status.live.players.map((p) => (
                  <tr key={p.guildId}>
                    <td className="mono">{p.guildId}</td>
                    <td>{p.state}</td>
                    <td className="num">{count(locale, p.queued)}</td>
                    <td>{p.nowPlaying ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}

/** Days and hours, which is the resolution anyone reads an uptime at. */
function uptime(t: Translate, seconds: number): string {
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);

  if (days > 0) {
    return t("admin.status.uptimeDays", { days, hours });
  }

  return hours > 0
    ? t("admin.status.uptimeHours", { hours, minutes })
    : t("admin.status.uptimeMinutes", { minutes });
}

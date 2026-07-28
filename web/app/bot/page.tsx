import { apiGet } from "../../lib/api";
import { count, when } from "../../lib/format";
import { currentLocale, pageTitle } from "../../lib/locale";
import { panel, type Query } from "../../lib/panel";

import { Fail } from "../components/Fail";
import { Frame, PageHead } from "../components/Frame";

type Status = {
  status: "ok" | "degraded" | "unknown";
  startedAt: string;
  uptimeSeconds: number;
  checkedAt: string | null;
  checks: { name: string; healthy: boolean; detail: string | null }[] | null;
  live: {
    guilds?: number;
    // Field names as `StatusPagePusher.Snapshot()` writes them — this blob is serialised by hand and
    // parsed back as a node, so nothing checks these for us.
    players?: { guildId: string; state: string; queued: number; nowPlaying: string | null }[];
  } | null;
  mood: string | null;
};

export const generateMetadata = () => pageTitle("nav.health");

/**
 * Is she up, and what is playing. Bot-wide, so bot tier only.
 *
 * The player rows come from `/api/status`, which strips them for anyone who is not a bot admin — so
 * this page renders whatever it is given rather than filtering here.
 */
export default async function HealthPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("bot", searchParams);
  const locale = await currentLocale();

  const status = await apiGet<Status>("/api/status");

  const uptime = (seconds: number) => {
    const days = Math.floor(seconds / 86400);
    const hours = Math.floor((seconds % 86400) / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);

    return days > 0
      ? t("health.dhm", { d: days, h: hours, m: minutes })
      : hours > 0
        ? t("health.hm", { h: hours, m: minutes })
        : t("health.m", { m: minutes });
  };

  return (
    <Frame session={session} current="bot" guild={guild} t={t}>
      <PageHead label={t("tier.bot")} title={t("health.title")} lead={t("health.lead")} />

      {status.ok ? (
        <>
          <section className="card">
            <div className="card-head">
              <h2>{t("health.status")}</h2>
              <span className="card-note">
                {status.data.checkedAt ? when(locale, status.data.checkedAt) : t("health.unchecked")}
              </span>
            </div>

            <div className="grid">
              <div className="stat">
                <div className="stat-label">{t("health.status")}</div>
                <div
                  className={
                    status.data.status === "ok"
                      ? "stat-value stat-ok"
                      : status.data.status === "degraded"
                        ? "stat-value stat-bad"
                        : "stat-value"
                  }
                >
                  {t(
                    status.data.status === "ok"
                      ? "health.up"
                      : status.data.status === "degraded"
                        ? "health.degraded"
                        : "health.unknown",
                  )}
                </div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("health.uptime")}</div>
                <div className="stat-value stat-value-sm mono">
                  {uptime(status.data.uptimeSeconds)}
                </div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("health.guilds")}</div>
                <div className="stat-value mono">
                  {status.data.live?.guilds === undefined
                    ? count(locale, session.guilds.length)
                    : count(locale, status.data.live.guilds)}
                </div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("health.mood")}</div>
                <div className="stat-value stat-value-sm">
                  {status.data.mood ?? t("health.quiet")}
                </div>
              </div>
            </div>
          </section>

          <section className="card">
            <div className="card-head">
              <h2>{t("health.checks")}</h2>
            </div>
            {status.data.checks === null || status.data.checks.length === 0 ? (
              <p className="empty">{t("health.checksEmpty")}</p>
            ) : (
              status.data.checks.map((check) => (
                <div className="row" key={check.name}>
                  <div className="row-main">
                    <div className="row-title mono">{check.name}</div>
                    {check.detail ? <div className="row-sub">{check.detail}</div> : null}
                  </div>
                  <span className={check.healthy ? "row-value stat-ok" : "row-value stat-bad"}>
                    {t(check.healthy ? "health.pass" : "health.fail")}
                  </span>
                </div>
              ))
            )}
          </section>

          <section className="card">
            <div className="card-head">
              <h2>{t("health.players")}</h2>
            </div>
            {!status.data.live?.players || status.data.live.players.length === 0 ? (
              <p className="empty">{t("health.playersEmpty")}</p>
            ) : (
              status.data.live.players.map((player) => (
                <div className="row" key={player.guildId}>
                  <div className="row-main">
                    <div className="row-title">{player.nowPlaying ?? t("health.noTrack")}</div>
                    <div className="row-sub mono">
                      {session.guilds.find((g) => g.guildId === player.guildId)?.name ??
                        player.guildId}
                      {player.queued > 0 ? ` · ${t("health.queued", { n: player.queued })}` : ""}
                    </div>
                  </div>
                  <span className="row-value mono">{player.state}</span>
                </div>
              ))
            )}
          </section>
        </>
      ) : status.failure === "error" ? (
        // The generic "try again" is wrong here. This page rendering at all proves the panel is up,
        // so a failed read means the bot is the part that is down — which is the answer the page
        // exists to give, not an error to retry past.
        <p className="notice notice-danger" role="alert">
          {t("health.failed")}
        </p>
      ) : (
        <Fail failure={status.failure} t={t} />
      )}
    </Frame>
  );
}

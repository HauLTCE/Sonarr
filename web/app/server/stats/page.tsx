import Link from "next/link";

import { apiGet } from "../../../lib/api";
import { count, day, hour, percent } from "../../../lib/format";
import { currentLocale, pageTitle } from "../../../lib/locale";
import { panel, type Query } from "../../../lib/panel";

import { Fail } from "../../components/Fail";
import { Frame, PageHead } from "../../components/Frame";

type Stats = {
  days: number;
  commands: { command: string; count: number }[];
  activity: { at: string; messages: number; voiceUsers: number; online: number }[];
  growth: { day: string; joined: number }[];
};

/** The windows the page offers. Anything else in the URL falls back to 30. */
const WINDOWS = [7, 30, 90] as const;

export const generateMetadata = () => pageTitle("nav.stats");

/**
 * Counts for one server. Aggregates only — the API returns messages per hour and joins per day, so
 * there is nothing here that could become a way to read one member's timeline.
 */
export default async function StatsPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("server/stats", searchParams);
  const locale = await currentLocale();
  const params = await searchParams;

  const id = guild!.guildId;
  const asked = Number.parseInt(typeof params.days === "string" ? params.days : "", 10);
  const days = (WINDOWS as readonly number[]).includes(asked) ? asked : 30;

  const stats = await apiGet<Stats>(`/api/admin/stats/${id}?days=${days}`);

  // Bars are drawn against the busiest row, so a quiet server still gets a readable chart.
  const busiest = stats.ok
    ? Math.max(1, ...stats.data.activity.map((a) => a.messages))
    : 1;
  const mostUsed = stats.ok ? Math.max(1, ...stats.data.commands.map((c) => c.count)) : 1;

  return (
    <Frame session={session} current="server/stats" guild={guild} t={t}>
      <PageHead
        label={t("tier.guild")}
        title={t("stats.title")}
        lead={t("stats.lead", { guild: guild!.name, days })}
      />

      <section className="card">
        <div className="card-head">
          <h2>{t("stats.window")}</h2>
        </div>
        <div className="pager">
          {WINDOWS.map((n) => (
            <Link
              key={n}
              className={n === days ? "btn btn-accent" : "btn"}
              href={`/server/stats?guild=${id}&days=${n}`}
              aria-current={n === days ? "true" : undefined}
            >
              {t(n === 7 ? "stats.days7" : n === 30 ? "stats.days30" : "stats.days90")}
            </Link>
          ))}
        </div>
      </section>

      {stats.ok ? (
        <>
          <section className="card">
            <div className="card-head">
              <h2>{t("stats.commands")}</h2>
            </div>
            {stats.data.commands.length === 0 ? (
              <p className="empty">{t("stats.commandsEmpty")}</p>
            ) : (
              stats.data.commands.map((c) => (
                <div className="row" key={c.command}>
                  <div className="row-main">
                    <div className="row-title mono">/{c.command}</div>
                    <div className="bar">
                      <span style={{ width: percent(c.count / mostUsed) }} />
                    </div>
                  </div>
                  <span className="row-value mono">
                    {t("stats.uses", { n: count(locale, c.count) })}
                  </span>
                </div>
              ))
            )}
          </section>

          <section className="card">
            <div className="card-head">
              <h2>{t("stats.activity")}</h2>
            </div>
            {stats.data.activity.length === 0 ? (
              <p className="empty">{t("stats.activityEmpty")}</p>
            ) : (
              // The bar heights are the only thing a sighted visitor needs; the numbers were in
              // `title`, which screen readers do not reliably announce, so this list read as 24
              // empty items. Each item now carries its hour and count as real text, hidden
              // visually. The growth section below is the same data shape rendered as rows, which
              // is what this now sounds like.
              <ol className="spark" aria-label={t("stats.activity")}>
                {stats.data.activity.map((a) => (
                  <li key={a.at}>
                    <span
                      className="spark-bar"
                      style={{ height: percent(a.messages / busiest) }}
                      aria-hidden="true"
                    />
                    <span className="at-only">
                      {hour(locale, a.at)}: {t("stats.messages", { n: count(locale, a.messages) })}
                    </span>
                  </li>
                ))}
              </ol>
            )}
          </section>

          <section className="card">
            <div className="card-head">
              <h2>{t("stats.growth")}</h2>
            </div>
            {stats.data.growth.length === 0 ? (
              <p className="empty">{t("stats.growthEmpty")}</p>
            ) : (
              stats.data.growth.map((g) => (
                <div className="row" key={g.day}>
                  <div className="row-main">
                    <div className="row-title">{day(locale, g.day)}</div>
                  </div>
                  <span className="row-value mono">
                    {t("stats.joined", { n: count(locale, g.joined) })}
                  </span>
                </div>
              ))
            )}
          </section>
        </>
      ) : (
        <Fail failure={stats.failure} t={t} />
      )}
    </Frame>
  );
}

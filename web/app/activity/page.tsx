import { apiGet } from "../../lib/api";
import { count, percent, when } from "../../lib/format";
import { currentLocale } from "../../lib/locale";
import { panel, type Query } from "../../lib/panel";

import { Fail } from "../components/Fail";
import { Frame, PageHead } from "../components/Frame";

type Overview = {
  level: number;
  xp: number;
  xpToNextLevel: number;
  fraction: number;
  rank: number;
  streakDays: number;
  messageCount: number;
  firstSeenAt: string | null;
  reminders: { jobId: number; runAt: string; text: string; recurrence: string | null }[];
};

/** Level, streak and reminders — the numbers `/rank` shows, in a place you can read at leisure. */
export default async function ActivityPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("activity", searchParams);
  const locale = await currentLocale();

  const head = (
    <PageHead label={t("tier.user")} title={t("activity.title")} lead={t("activity.lead")} />
  );

  if (guild === null) {
    return (
      <Frame session={session} current="activity" guild={null} t={t}>
        {head}
        <p className="notice">{t("state.noGuilds")}</p>
      </Frame>
    );
  }

  const overview = await apiGet<Overview>(`/api/me/overview?guildId=${guild.guildId}`);

  return (
    <Frame session={session} current="activity" guild={guild} t={t}>
      {head}

      {overview.ok ? (
        <>
          <section className="card">
            <div className="card-head">
              <h2>{t("activity.level")}</h2>
              <span className="card-note">{guild.name}</span>
            </div>

            <div className="grid">
              <div className="stat">
                <div className="stat-label">{t("activity.level")}</div>
                <div className="stat-value mono">{count(locale, overview.data.level)}</div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("activity.rank")}</div>
                <div className="stat-value mono">#{count(locale, overview.data.rank)}</div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("activity.xp")}</div>
                <div className="stat-value mono">{count(locale, overview.data.xp)}</div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("activity.streak")}</div>
                <div className="stat-value mono">{count(locale, overview.data.streakDays)}</div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("activity.messages")}</div>
                <div className="stat-value mono">{count(locale, overview.data.messageCount)}</div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("activity.firstSeen")}</div>
                <div className="stat-value stat-value-sm">
                  {when(locale, overview.data.firstSeenAt)}
                </div>
              </div>
            </div>

            <div className="progress">
              <div className="bar">
                <span style={{ width: percent(overview.data.fraction) }} />
              </div>
              <p className="hint">
                {t("activity.toNext", {
                  n: count(locale, overview.data.xpToNextLevel),
                  level: count(locale, overview.data.level + 1),
                })}
              </p>
            </div>
          </section>

          <section className="card">
            <div className="card-head">
              <h2>{t("activity.reminders")}</h2>
            </div>
            {overview.data.reminders.length === 0 ? (
              <p className="empty">{t("activity.remindersEmpty")}</p>
            ) : (
              overview.data.reminders.map((r) => (
                <div className="row" key={r.jobId}>
                  <div className="row-main">
                    <div className="row-title">{r.text}</div>
                    <div className="row-sub mono">
                      {t("activity.due", { when: when(locale, r.runAt) })}
                      {r.recurrence ? ` · ${r.recurrence}` : ""}
                    </div>
                  </div>
                </div>
              ))
            )}
          </section>
        </>
      ) : (
        <Fail failure={overview.failure} t={t} />
      )}
    </Frame>
  );
}

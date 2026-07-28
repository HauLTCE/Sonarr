import { apiGet } from "@/lib/api";
import { count, percent, when } from "@/lib/format";
import { guildScope, type SearchParams } from "@/lib/guilds";
import { translator } from "@/lib/strings";

import { Failure } from "../components/Failure";
import { GuildPicker } from "../components/GuildPicker";
import { NoGuilds } from "../components/NoGuilds";

type Overview = {
  level: number;
  xp: number;
  xpIntoLevel: number;
  xpForLevel: number;
  xpToNextLevel: number;
  fraction: number;
  rank: number;
  streakDays: number;
  voiceMinutes: number;
  messageCount: number;
  firstSeenAt: string | null;
  lastActiveAt: string | null;
  reminders: {
    jobId: number;
    runAt: string;
    text: string;
    schedule: string | null;
    recurrence: string | null;
  }[];
};

/** Level, rank, streak, activity and the user's own reminders — the landing page (checklist 289). */
export default async function OverviewPage({ searchParams }: { searchParams: SearchParams }) {
  const scope = await guildScope(searchParams);

  if (!scope.ok) {
    return scope.failure === null ? (
      <NoGuilds locale={scope.locale} />
    ) : (
      <Failure locale={scope.locale} failure={scope.failure} />
    );
  }

  const { locale, guild, options } = scope;
  const t = translator(locale);
  const result = await apiGet<Overview>(`/api/me/overview?guildId=${guild.guildId}`);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  const me = result.data;

  return (
    <>
      <h1>{t("overview.title")}</h1>

      <GuildPicker locale={locale} path="/" options={options} selected={guild.guildId} />

      <section className="card">
        <dl className="grid">
          <div className="stat">
            <dt>{t("overview.level")}</dt>
            <dd>{count(locale, me.level)}</dd>
          </div>
          <div className="stat">
            <dt>{t("overview.rank")}</dt>
            <dd>{me.rank > 0 ? `#${count(locale, me.rank)}` : "—"}</dd>
          </div>
          <div className="stat">
            <dt>{t("overview.xp")}</dt>
            <dd>{count(locale, me.xp)}</dd>
          </div>
          <div className="stat">
            <dt>{t("overview.streak")}</dt>
            <dd>
              {me.streakDays > 0
                ? t("overview.streakDays", { count: count(locale, me.streakDays) })
                : t("overview.streakNone")}
            </dd>
          </div>
          <div className="stat">
            <dt>{t("overview.messages")}</dt>
            <dd>{count(locale, me.messageCount)}</dd>
          </div>
          <div className="stat">
            <dt>{t("overview.voice")}</dt>
            <dd>{t("overview.voiceMinutes", { count: count(locale, me.voiceMinutes) })}</dd>
          </div>
        </dl>

        {/* The bar is decoration; the sentence under it is the actual information. */}
        <div
          className="bar"
          role="img"
          aria-label={t("overview.progressLabel", { level: me.level })}
        >
          <span style={{ width: percent(me.fraction) }} />
        </div>
        <p className="hint">
          {t("overview.toNextLevel", { count: count(locale, me.xpToNextLevel) })}
        </p>

        <p className="hint">
          {t("overview.joined")}: {when(locale, me.firstSeenAt)} · {t("overview.lastActive")}:{" "}
          {when(locale, me.lastActiveAt)}
        </p>
      </section>

      <section className="card">
        <h2>{t("overview.reminders")}</h2>

        {me.reminders.length === 0 ? (
          <p className="empty">{t("overview.remindersEmpty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("common.when")}</th>
                  <th scope="col">{t("common.what")}</th>
                </tr>
              </thead>
              <tbody>
                {me.reminders.map((r) => (
                  <tr key={r.jobId}>
                    <td>
                      {when(locale, r.runAt)}
                      {r.recurrence ? <> · <span className="pill">{r.recurrence}</span></> : null}
                    </td>
                    <td>{r.text}</td>
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

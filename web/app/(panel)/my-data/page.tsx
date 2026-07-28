import { apiGet } from "@/lib/api";
import { count, day, when } from "@/lib/format";
import { currentLocale } from "@/lib/locale";
import { translator } from "@/lib/strings";

import { Failure } from "../../components/Failure";

type Export = {
  generatedAt: string;
  memberships: {
    guildId: number;
    guildName: string;
    username: string;
    displayName: string;
    firstSeenAt: string;
    lastActiveAt: string;
    messageCount: number;
    timezone: string | null;
    birthday: string | null;
  }[];
  levels: { guildId: number; xp: number; level: number; voiceMinutes: number; streakDays: number }[];
  facts: {
    guildId: number;
    predicate: string;
    value: string;
    confidence: number;
    learnedAt: string;
  }[];
  counts: {
    chatEpisodes: number;
    relationshipEvents: number;
    tracksPlayed: number;
    trackRatings: number;
    savedQuotes: number;
    reminders: number;
    capsules: number;
    modCases: number;
  };
  sessions: { createdAt: string; expiresAt: string; userAgent: string | null }[];
};

/**
 * Everything stored about the visitor, read live (checklist 290).
 *
 * Guilds are shown by name throughout. `/api/me/data` is the export payload, where ids are JSON
 * numbers — a snowflake past 2^53 is not exactly representable in JavaScript, so the ids from this
 * endpoint are only ever used to join rows together, never rendered.
 */
export default async function MyDataPage() {
  const [locale, result] = await Promise.all([currentLocale(), apiGet<Export>("/api/me/data")]);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  const t = translator(locale);
  const data = result.data;
  const names = new Map(data.memberships.map((m) => [m.guildId, m.guildName]));
  const named = (guildId: number) => names.get(guildId) ?? "—";

  const counts: [string, number][] = [
    [t("myData.chatEpisodes"), data.counts.chatEpisodes],
    [t("myData.relationshipEvents"), data.counts.relationshipEvents],
    [t("myData.tracksPlayed"), data.counts.tracksPlayed],
    [t("myData.trackRatings"), data.counts.trackRatings],
    [t("myData.savedQuotes"), data.counts.savedQuotes],
    [t("myData.remindersCount"), data.counts.reminders],
    [t("myData.capsules"), data.counts.capsules],
    [t("myData.modCases"), data.counts.modCases],
  ];

  return (
    <>
      <h1>{t("myData.title")}</h1>
      <p className="lead">{t("myData.lead")}</p>
      <p className="hint">{t("myData.generatedAt", { at: when(locale, data.generatedAt) })}</p>

      <section className="card">
        <h2>{t("myData.memberships")}</h2>
        <div className="scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">{t("guild.label")}</th>
                <th scope="col">{t("myData.name")}</th>
                <th scope="col">{t("overview.joined")}</th>
                <th scope="col">{t("overview.lastActive")}</th>
                <th scope="col" className="num">
                  {t("overview.messages")}
                </th>
                <th scope="col">{t("myData.timezone")}</th>
                <th scope="col">{t("myData.birthday")}</th>
              </tr>
            </thead>
            <tbody>
              {data.memberships.map((m) => (
                <tr key={m.guildName + m.firstSeenAt}>
                  <td>{m.guildName}</td>
                  <td>
                    {m.displayName}
                    <br />
                    <span className="hint">{m.username}</span>
                  </td>
                  <td>{when(locale, m.firstSeenAt)}</td>
                  <td>{when(locale, m.lastActiveAt)}</td>
                  <td className="num">{count(locale, m.messageCount)}</td>
                  <td>{m.timezone ?? t("common.none")}</td>
                  <td>{m.birthday ? day(locale, m.birthday) : t("common.none")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card">
        <h2>{t("myData.levels")}</h2>
        <div className="scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">{t("guild.label")}</th>
                <th scope="col" className="num">
                  {t("overview.level")}
                </th>
                <th scope="col" className="num">
                  {t("overview.xp")}
                </th>
                <th scope="col" className="num">
                  {t("overview.voice")}
                </th>
                <th scope="col" className="num">
                  {t("overview.streak")}
                </th>
              </tr>
            </thead>
            <tbody>
              {data.levels.map((l) => (
                <tr key={l.guildId}>
                  <td>{named(l.guildId)}</td>
                  <td className="num">{count(locale, l.level)}</td>
                  <td className="num">{count(locale, l.xp)}</td>
                  <td className="num">
                    {t("overview.voiceMinutes", { count: count(locale, l.voiceMinutes) })}
                  </td>
                  <td className="num">{count(locale, l.streakDays)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card">
        <h2>{t("myData.facts")}</h2>
        {data.facts.length === 0 ? (
          <p className="empty">{t("myData.factsEmpty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("guild.label")}</th>
                  <th scope="col">{t("myData.predicate")}</th>
                  <th scope="col">{t("myData.value")}</th>
                  <th scope="col">{t("sonarr.confidenceShort")}</th>
                  <th scope="col">{t("myData.learnedAt")}</th>
                </tr>
              </thead>
              <tbody>
                {data.facts.map((f) => (
                  <tr key={`${f.guildId}:${f.predicate}`}>
                    <td>{named(f.guildId)}</td>
                    <td className="mono">{f.predicate}</td>
                    <td>{f.value}</td>
                    <td className="num">{f.confidence.toFixed(2)}</td>
                    <td>{when(locale, f.learnedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2>{t("myData.counts")}</h2>
        <dl className="grid">
          {counts.map(([label, value]) => (
            <div className="stat" key={label}>
              <dt>{label}</dt>
              <dd>{count(locale, value)}</dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="card">
        <h2>{t("myData.sessions")}</h2>
        <div className="scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">{t("myData.sessionStarted")}</th>
                <th scope="col">{t("myData.sessionExpires")}</th>
                <th scope="col">{t("myData.device")}</th>
              </tr>
            </thead>
            <tbody>
              {data.sessions.map((s) => (
                <tr key={s.createdAt}>
                  <td>{when(locale, s.createdAt)}</td>
                  <td>{when(locale, s.expiresAt)}</td>
                  <td className="hint">{s.userAgent ?? t("common.none")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <p>
        <a href="/api/me/export">{t("myData.download")}</a>
      </p>
    </>
  );
}

import { apiGet } from "@/lib/api";
import { count, day, when } from "@/lib/format";
import { guildScope, one, type SearchParams } from "@/lib/guilds";
import { translator, type Translate } from "@/lib/strings";

import { Failure } from "../../../components/Failure";
import { GuildPicker } from "../../../components/GuildPicker";
import { NoGuilds } from "../../../components/NoGuilds";

type Stats = {
  days: number;
  commands: { command: string; count: number }[];
  activity: { at: string; messages: number; voiceUsers: number; online: number }[];
  growth: { day: string; joined: number }[];
};

/**
 * Command usage, activity and member growth for one server (checklist 299).
 *
 * Every number on this page is a total. There is no route that would show one member's activity and
 * this page does not try to reconstruct one from the hourly samples (docs/06) — the note under the
 * charts says so, because an admin who assumes otherwise will go looking.
 */
export default async function AdminStatsPage({ searchParams }: { searchParams: SearchParams }) {
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

  const params = await searchParams;
  const days = Math.min(400, Math.max(1, Number.parseInt(one(params.days) ?? "30", 10) || 30));

  const result = await apiGet<Stats>(`/api/admin/stats/${guild.guildId}?days=${days}`);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  const stats = result.data;

  return (
    <>
      <h1>{t("admin.stats.title")}</h1>

      <GuildPicker locale={locale} path="/admin/stats" options={options} selected={guild.guildId} />

      <p className="hint">{t("admin.stats.window", { days: stats.days })}</p>

      <section className="card">
        <h2>{t("admin.stats.commands")}</h2>
        {stats.commands.length === 0 ? (
          <p className="empty">{t("music.empty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("errors.command")}</th>
                  <th scope="col" className="num">
                    {t("admin.stats.uses")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {stats.commands.map((c) => (
                  <tr key={c.command}>
                    <td className="mono">{c.command}</td>
                    <td className="num">{count(locale, c.count)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2>{t("admin.stats.activity")}</h2>
        {stats.activity.length === 0 ? (
          <p className="empty">{t("music.empty")}</p>
        ) : (
          <>
            <Bars
              t={t}
              label={t("admin.stats.messages")}
              points={stats.activity.map((a) => ({
                value: a.messages,
                title: `${when(locale, a.at)}: ${count(locale, a.messages)}`,
              }))}
            />
            <Bars
              t={t}
              label={t("admin.stats.voice")}
              points={stats.activity.map((a) => ({
                value: a.voiceUsers,
                title: `${when(locale, a.at)}: ${count(locale, a.voiceUsers)}`,
              }))}
            />
          </>
        )}
      </section>

      <section className="card">
        <h2>{t("admin.stats.growth")}</h2>
        {stats.growth.length === 0 ? (
          <p className="empty">{t("music.empty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("admin.stats.day")}</th>
                  <th scope="col" className="num">
                    {t("admin.stats.joined")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {stats.growth.map((g) => (
                  <tr key={g.day}>
                    <td>{day(locale, g.day)}</td>
                    <td className="num">{count(locale, g.joined)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <p className="hint">{t("admin.stats.privacyNote")}</p>
    </>
  );
}

/**
 * A row of CSS bars with a text summary beside it.
 *
 * The bars carry `aria-hidden`: a screen reader gets the summary sentence instead, which says more
 * than a hundred unlabelled divs would. Each bar keeps a `title` so a mouse can read one hour.
 */
function Bars({
  t,
  label,
  points,
}: {
  t: Translate;
  label: string;
  points: { value: number; title: string }[];
}) {
  const peak = Math.max(1, ...points.map((p) => p.value));
  const total = points.reduce((sum, p) => sum + p.value, 0);

  return (
    <div>
      <h3>{label}</h3>
      <div className="chart" aria-hidden="true">
        {points.map((p) => (
          <div
            key={p.title}
            title={p.title}
            // A floor of 1% so an hour with nothing in it is still visibly an hour.
            style={{ height: `${Math.max(1, Math.round((p.value / peak) * 100))}%` }}
          />
        ))}
      </div>
      <p className="hint">{t("admin.stats.summary", { total, peak })}</p>
    </div>
  );
}

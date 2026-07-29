import { apiGet } from "../lib/api";
import { count, when } from "../lib/format";
import { currentLocale, pageTitle } from "../lib/locale";
import { panel, type Query } from "../lib/panel";

import { Fail } from "./components/Fail";
import { Frame, PageHead } from "./components/Frame";

type SonarrAndMe = {
  relationship: string;
  tier: string | null;
  nickname: string | null;
  opener: string | null;
  facts: { predicate: string; value: string; confidence: number; learnedAt: string }[];
};

type Overview = { messageCount: number };

export const generateMetadata = () => pageTitle("nav.you");

/**
 * The panel root: who you are to Sonarr, in her words.
 *
 * It used to end with a "where to go next" card explaining the rail — click a segment, use the
 * arrow keys. That card is gone with the rail it described. Labelled tabs across the top need no
 * instructions, and a page that opens by teaching you its own navigation is a page admitting the
 * navigation is not obvious.
 */
export default async function YouPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("", searchParams);
  const locale = await currentLocale();

  if (guild === null) {
    return (
      <Frame session={session} current="" guild={null} t={t}>
        <PageHead label={t("tier.user")} title={t("you.title")} lead={t("you.lead")} />
        <p className="notice">{t("state.noGuilds")}</p>
      </Frame>
    );
  }

  const [me, overview] = await Promise.all([
    apiGet<SonarrAndMe>(`/api/me/sonarr?guildId=${guild.guildId}`),
    apiGet<Overview>(`/api/me/overview?guildId=${guild.guildId}`),
  ]);

  return (
    <Frame session={session} current="" guild={guild} t={t}>
      <PageHead label={t("tier.user")} title={t("you.title")} lead={t("you.lead")} />

      {me.ok ? (
        <>
          <section className="card">
            <div className="card-head">
              <h2>{t("you.who")}</h2>
              <span className="card-note">{guild.name}</span>
            </div>
            <p>{me.data.relationship}</p>
          </section>

          <section className="card">
            <div className="grid">
              <div className="stat">
                <div className="stat-label">{t("you.nickname")}</div>
                <div className="stat-value">{me.data.nickname ?? t("you.none")}</div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("you.mood")}</div>
                <div className="stat-value">{me.data.tier ?? t("you.none")}</div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("you.facts")}</div>
                <div className="stat-value mono">{count(locale, me.data.facts.length)}</div>
                <div className="stat-sub">{t("you.factsSub")}</div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("you.talked")}</div>
                <div className="stat-value mono">
                  {overview.ok ? count(locale, overview.data.messageCount) : "—"}
                </div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("you.servers")}</div>
                <div className="stat-value mono">{count(locale, session.guilds.length)}</div>
              </div>
              <div className="stat">
                <div className="stat-label">{t("you.sessionEnds")}</div>
                <div className="stat-value stat-value-sm">{when(locale, session.expiresAt)}</div>
              </div>
            </div>
          </section>
        </>
      ) : (
        <Fail failure={me.failure} t={t} />
      )}

    </Frame>
  );
}

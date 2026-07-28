import { apiGet } from "../../lib/api";
import { count } from "../../lib/format";
import { currentLocale, pageTitle } from "../../lib/locale";
import { panel, type Query } from "../../lib/panel";

import { Fail } from "../components/Fail";
import { Frame, PageHead } from "../components/Frame";

type Music = {
  history: { title: string; uri: string; plays: number }[];
  ratings: { title: string; uri: string; vote: number; score: number; at: string }[];
  serverTop: { title: string; uri: string; likes: number; dislikes: number; score: number }[];
};

export const generateMetadata = () => pageTitle("nav.music");

/** What you have played and rated, plus what this server rates highest. */
export default async function MusicPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("music", searchParams);
  const locale = await currentLocale();

  const head = <PageHead label={t("tier.user")} title={t("music.title")} lead={t("music.lead")} />;

  if (guild === null) {
    return (
      <Frame session={session} current="music" guild={null} t={t}>
        {head}
        <p className="notice">{t("state.noGuilds")}</p>
      </Frame>
    );
  }

  const music = await apiGet<Music>(`/api/me/music?guildId=${guild.guildId}`);

  return (
    <Frame session={session} current="music" guild={guild} t={t}>
      {head}

      {music.ok ? (
        <>
          <section className="card">
            <div className="card-head">
              <h2>{t("music.history")}</h2>
              <span className="card-note">{guild.name}</span>
            </div>
            {music.data.history.length === 0 ? (
              <p className="empty">{t("music.historyEmpty")}</p>
            ) : (
              music.data.history.map((track) => (
                <div className="row" key={`${track.uri}-${track.title}`}>
                  <div className="row-main">
                    <div className="row-title">{track.title}</div>
                  </div>
                  <span className="row-value mono">
                    {t("music.plays", { n: count(locale, track.plays) })}
                  </span>
                </div>
              ))
            )}
          </section>

          <section className="card">
            <div className="card-head">
              <h2>{t("music.ratings")}</h2>
            </div>
            {music.data.ratings.length === 0 ? (
              <p className="empty">{t("music.ratingsEmpty")}</p>
            ) : (
              music.data.ratings.map((track) => (
                <div className="row" key={`${track.uri}-${track.at}`}>
                  <div className="row-main">
                    <div className="row-title">{track.title}</div>
                  </div>
                  <span className="row-value mono">
                    {track.vote > 0 ? t("music.liked") : t("music.disliked")}
                  </span>
                </div>
              ))
            )}
          </section>

          <section className="card">
            <div className="card-head">
              <h2>{t("music.top")}</h2>
              <span className="card-note">{guild.name}</span>
            </div>
            {music.data.serverTop.length === 0 ? (
              <p className="empty">{t("music.topEmpty")}</p>
            ) : (
              music.data.serverTop.map((track) => (
                <div className="row" key={`${track.uri}-${track.title}`}>
                  <div className="row-main">
                    <div className="row-title">{track.title}</div>
                    <div className="row-sub mono">
                      {t("music.votes", {
                        up: count(locale, track.likes),
                        down: count(locale, track.dislikes),
                      })}
                    </div>
                  </div>
                  <span className="row-value mono">{count(locale, track.score)}</span>
                </div>
              ))
            )}
          </section>
        </>
      ) : (
        <Fail failure={music.failure} t={t} />
      )}
    </Frame>
  );
}

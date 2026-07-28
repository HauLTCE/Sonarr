import { apiGet } from "@/lib/api";
import { count, when } from "@/lib/format";
import { guildScope, type SearchParams } from "@/lib/guilds";
import { translator, type Translate } from "@/lib/strings";

import { Failure } from "../../components/Failure";
import { GuildPicker } from "../../components/GuildPicker";
import { NoGuilds } from "../../components/NoGuilds";

type Music = {
  history: { title: string; uri: string; plays: number }[];
  ratings: {
    title: string;
    uri: string;
    vote: number;
    likes: number;
    dislikes: number;
    score: number;
    at: string;
  }[];
  serverTop: { title: string; uri: string; likes: number; dislikes: number; score: number }[];
};

/** My history, my ratings, and what the server rates highest (checklist 293). */
export default async function MusicPage({ searchParams }: { searchParams: SearchParams }) {
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
  const result = await apiGet<Music>(`/api/me/music?guildId=${guild.guildId}`);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  const music = result.data;

  return (
    <>
      <h1>{t("music.title")}</h1>

      <GuildPicker locale={locale} path="/music" options={options} selected={guild.guildId} />

      <section className="card">
        <h2>{t("music.myHistory")}</h2>
        {music.history.length === 0 ? (
          <p className="empty">{t("music.empty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("music.track")}</th>
                  <th scope="col" className="num">
                    {t("music.playsHeader")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {music.history.map((track) => (
                  <tr key={track.uri}>
                    <td>
                      <Track title={track.title} uri={track.uri} t={t} />
                    </td>
                    <td className="num">{count(locale, track.plays)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2>{t("music.myRatings")}</h2>
        {music.ratings.length === 0 ? (
          <p className="empty">{t("music.empty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("music.track")}</th>
                  <th scope="col">{t("music.myVote")}</th>
                  <th scope="col">{t("music.serverVotes")}</th>
                  <th scope="col">{t("common.when")}</th>
                </tr>
              </thead>
              <tbody>
                {music.ratings.map((track) => (
                  <tr key={track.uri}>
                    <td>
                      <Track title={track.title} uri={track.uri} t={t} />
                    </td>
                    {/* The word carries the meaning, not the colour or an arrow glyph alone. */}
                    <td>{track.vote > 0 ? t("music.liked") : t("music.disliked")}</td>
                    <td>
                      {t("music.tally", {
                        likes: count(locale, track.likes),
                        dislikes: count(locale, track.dislikes),
                      })}
                    </td>
                    <td>{when(locale, track.at)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2>{t("music.serverTop")}</h2>
        {music.serverTop.length === 0 ? (
          <p className="empty">{t("music.empty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("music.track")}</th>
                  <th scope="col">{t("music.serverVotes")}</th>
                  <th scope="col" className="num">
                    {t("music.score")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {music.serverTop.map((track) => (
                  <tr key={track.uri}>
                    <td>
                      <Track title={track.title} uri={track.uri} t={t} />
                    </td>
                    <td>
                      {t("music.tally", {
                        likes: count(locale, track.likes),
                        dislikes: count(locale, track.dislikes),
                      })}
                    </td>
                    <td className="num">{track.score > 0 ? `+${track.score}` : track.score}</td>
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

/**
 * A track title, linked out when the uri is one a browser can open.
 *
 * The scheme check is the point: a uri comes from whatever the player resolved, and rendering an
 * unchecked one into href is how `javascript:` ends up in a link.
 */
function Track({ title, uri, t }: { title: string; uri: string; t: Translate }) {
  const shown = title.trim().length > 0 ? title : t("music.untitled");
  const web = uri.startsWith("https://") || uri.startsWith("http://");

  return web ? (
    <a href={uri} rel="noreferrer noopener" target="_blank">
      {shown}
    </a>
  ) : (
    <>{shown}</>
  );
}

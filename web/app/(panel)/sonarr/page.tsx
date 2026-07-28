import { apiGet } from "@/lib/api";
import { when } from "@/lib/format";
import { guildScope, type SearchParams } from "@/lib/guilds";
import { translator } from "@/lib/strings";

import { Failure } from "../../components/Failure";
import { GuildPicker } from "../../components/GuildPicker";
import { NoGuilds } from "../../components/NoGuilds";

import { ForgetButton } from "./ForgetButton";

type SonarrAndMe = {
  relationship: string;
  tier: string | null;
  nickname: string | null;
  opener: string | null;
  facts: { predicate: string; value: string; confidence: number; learnedAt: string }[];
};

/**
 * What she thinks of the visitor, in her own words, with a forget button per fact (checklist 291).
 *
 * Only ever about the person asking. There is no route that renders this for someone else, and the
 * admin panel does not get one either (docs/06).
 */
export default async function SonarrPage({ searchParams }: { searchParams: SearchParams }) {
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
  const result = await apiGet<SonarrAndMe>(`/api/me/sonarr?guildId=${guild.guildId}`);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  const her = result.data;

  return (
    <>
      <h1>{t("sonarr.title")}</h1>

      <GuildPicker locale={locale} path="/sonarr" options={options} selected={guild.guildId} />

      <section className="card">
        <h2>{t("sonarr.relationship")}</h2>
        <p>{her.relationship}</p>

        <dl className="grid">
          <div className="stat">
            <dt>{t("sonarr.nickname")}</dt>
            <dd style={{ fontSize: "1.1rem" }}>
              {her.nickname ?? <span className="empty">{t("sonarr.nicknameNone")}</span>}
            </dd>
          </div>
          {her.tier && (
            <div className="stat">
              <dt>{t("sonarr.tier")}</dt>
              <dd style={{ fontSize: "1.1rem" }}>{her.tier}</dd>
            </div>
          )}
        </dl>

        {her.opener && (
          <p className="hint">
            {t("sonarr.opener")}: {her.opener}
          </p>
        )}
      </section>

      <section className="card">
        <h2>{t("sonarr.facts")}</h2>

        {her.facts.length === 0 ? (
          <p className="empty">{t("sonarr.factsEmpty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("myData.predicate")}</th>
                  <th scope="col">{t("myData.value")}</th>
                  <th scope="col">{t("sonarr.confidenceShort")}</th>
                  <th scope="col">{t("myData.learnedAt")}</th>
                  <th scope="col">
                    <span className="hint">{t("common.actions")}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {her.facts.map((f) => (
                  <tr key={f.predicate}>
                    <td className="mono">{f.predicate}</td>
                    <td>{f.value}</td>
                    <td className="num">{f.confidence.toFixed(2)}</td>
                    <td>{when(locale, f.learnedAt)}</td>
                    <td>
                      <ForgetButton
                        locale={locale}
                        guildId={guild.guildId}
                        predicate={f.predicate}
                      />
                    </td>
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

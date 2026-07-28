import { apiGet } from "../../lib/api";
import { currentLocale } from "../../lib/locale";
import { panel, type Query } from "../../lib/panel";
import { when } from "../../lib/format";

import { Fail } from "../components/Fail";
import { Frame, PageHead } from "../components/Frame";
import { ForgetFact } from "./ForgetFact";

type SonarrAndMe = {
  opener: string | null;
  facts: { predicate: string; value: string; confidence: number; learnedAt: string }[];
};

/** The facts she has picked up, each with the button that makes her drop it. */
export default async function MemoryPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("memory", searchParams);
  const locale = await currentLocale();

  const head = <PageHead label={t("tier.user")} title={t("memory.title")} lead={t("memory.lead")} />;

  if (guild === null) {
    return (
      <Frame session={session} current="memory" guild={null} t={t}>
        {head}
        <p className="notice">{t("state.noGuilds")}</p>
      </Frame>
    );
  }

  const me = await apiGet<SonarrAndMe>(`/api/me/sonarr?guildId=${guild.guildId}`);

  return (
    <Frame session={session} current="memory" guild={guild} t={t}>
      {head}

      {me.ok ? (
        <section className="card">
          <div className="card-head">
            <h2>{t("you.facts")}</h2>
            <span className="card-note">{guild.name}</span>
          </div>

          {me.data.facts.length === 0 ? (
            <p className="empty">{t("memory.empty")}</p>
          ) : (
            me.data.facts.map((fact) => (
              <div className="row" key={fact.predicate}>
                <div className="row-main">
                  <div className="row-title">{fact.value}</div>
                  <div className="row-sub mono">
                    {fact.predicate} · {when(locale, fact.learnedAt)}
                  </div>
                </div>
                <ForgetFact
                  predicate={fact.predicate}
                  guildId={guild.guildId}
                  labels={{
                    forget: t("memory.forget"),
                    forgetting: t("memory.forgetting"),
                    hint: t("memory.forgetHint"),
                    failed: t("memory.failed"),
                  }}
                />
              </div>
            ))
          )}
        </section>
      ) : (
        <Fail failure={me.failure} t={t} />
      )}
    </Frame>
  );
}

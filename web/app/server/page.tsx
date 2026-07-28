import { apiGet } from "../../lib/api";
import { count } from "../../lib/format";
import { currentLocale } from "../../lib/locale";
import { panel, type Query } from "../../lib/panel";

import { Fail } from "../components/Fail";
import { Frame, PageHead } from "../components/Frame";

type Flags = { feature: string; enabled: boolean; source: string }[];
type Config = Record<string, { value: string | null; kind: string }>;
type Cases = { totalCount: number };

/**
 * The guild tier's front page: how Sonarr behaves here, in sentences rather than a settings table.
 *
 * Behaviour is where the values get changed. This page answers "what is on right now", which is the
 * question an admin actually opens the panel with.
 */
export default async function ServerPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("server", searchParams);
  const locale = await currentLocale();

  // panel() redirects when a guild-tier page has no manageable guild, so this is non-null here.
  const id = guild!.guildId;

  const [flags, config, cases] = await Promise.all([
    apiGet<Flags>(`/api/admin/flags/${id}`),
    apiGet<Config>(`/api/admin/config/${id}`),
    apiGet<Cases>(`/api/admin/cases/${id}?page=1`),
  ]);

  const on = (feature: string) =>
    flags.ok ? flags.data.find((f) => f.feature === feature)?.enabled === true : false;

  const channel = (key: string) => {
    const value = config.ok ? config.data[key]?.value : null;

    return value ? `#${value}` : null;
  };

  return (
    <Frame session={session} current="server" guild={guild} t={t}>
      <PageHead
        label={t("tier.guild")}
        title={t("server.title")}
        lead={t("server.lead", { guild: guild!.name })}
      />

      {flags.ok ? (
        <section className="card">
          <div className="card-head">
            <h2>{t("server.summary")}</h2>
            <span className="card-note">{guild!.name}</span>
          </div>

          <div className="grid">
            <div className="stat">
              <div className="stat-label">{t("nav.behaviour")}</div>
              <div className="stat-value stat-value-sm">
                {on("chat") ? t("server.chatOn") : t("server.chatOff")}
              </div>
            </div>
            <div className="stat">
              <div className="stat-label">{t("nav.stats")}</div>
              <div className="stat-value stat-value-sm">
                {on("levels") ? t("server.levelsOn") : t("server.levelsOff")}
              </div>
            </div>
            <div className="stat">
              <div className="stat-label">{t("nav.music")}</div>
              <div className="stat-value stat-value-sm">
                {on("music") ? t("server.musicOn") : t("server.musicOff")}
              </div>
            </div>
            <div className="stat">
              <div className="stat-label">{t("server.logChannel")}</div>
              <div className="stat-value stat-value-sm mono">
                {channel("log_channel") ?? t("server.notSet")}
              </div>
            </div>
            <div className="stat">
              <div className="stat-label">{t("server.cases")}</div>
              <div className="stat-value mono">
                {cases.ok ? count(locale, cases.data.totalCount) : "—"}
              </div>
            </div>
          </div>
        </section>
      ) : (
        <Fail failure={flags.failure} t={t} />
      )}

      <section className="card">
        <div className="card-head">
          <h2>{t("server.scopeTitle")}</h2>
        </div>
        <p className="hint">{t("server.onlyThis", { guild: guild!.name })}</p>
      </section>
    </Frame>
  );
}

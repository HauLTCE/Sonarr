import Link from "next/link";

import { apiGet } from "../../../lib/api";
import { when } from "../../../lib/format";
import { currentLocale } from "../../../lib/locale";
import { panel, type Query } from "../../../lib/panel";

import { Fail } from "../../components/Fail";
import { Frame, PageHead } from "../../components/Frame";

type Audit = {
  auditId: number;
  userId: string;
  guildId: string | null;
  action: string;
  target: string | null;
  detail: string | null;
  at: string;
}[];

/** What the API returns per request when `take` is not given, and what this page asks for. */
const TAKE = 50;

/**
 * Every change made through this panel. Bot tier only, because the log spans guilds — there is no
 * guild id a server manager could be checked against, so `/api/admin/audit` gates on the bot tier
 * and this page matches it.
 *
 * Skip/take rather than page numbers, because that is what the endpoint takes; the pager only
 * renders the directions that exist, so there is no "next" onto an empty page.
 */
export default async function AuditPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("bot/audit", searchParams);
  const locale = await currentLocale();
  const params = await searchParams;

  const asked = Number.parseInt(typeof params.skip === "string" ? params.skip : "0", 10);
  const skip = Number.isFinite(asked) && asked > 0 ? asked : 0;

  const rows = await apiGet<Audit>(`/api/admin/audit?skip=${skip}&take=${TAKE}`);

  /** The name of a server this admin shares, falling back to the raw id for the ones they do not. */
  const serverName = (guildId: string | null) =>
    guildId === null
      ? t("audit.botWide")
      : (session.guilds.find((g) => g.guildId === guildId)?.name ?? guildId);

  return (
    <Frame session={session} current="bot/audit" guild={guild} t={t}>
      <PageHead label={t("tier.bot")} title={t("audit.title")} lead={t("audit.lead")} />

      {rows.ok ? (
        <section className="card">
          <div className="card-head">
            <h2>{t("audit.title")}</h2>
            {skip > 0 ? (
              <span className="card-note mono">{t("audit.from", { n: skip + 1 })}</span>
            ) : null}
          </div>

          {rows.data.length === 0 ? (
            <p className="empty">{skip > 0 ? t("audit.endEmpty") : t("audit.empty")}</p>
          ) : (
            rows.data.map((row) => (
              <div className="row" key={row.auditId}>
                <div className="row-main">
                  <div className="row-title">
                    {row.action}
                    {row.target ? ` · ${row.target}` : ""}
                  </div>
                  {row.detail ? <div className="row-sub">{row.detail}</div> : null}
                  <div className="row-sub mono">
                    {t("audit.who")} {row.userId} · {t("audit.server")} {serverName(row.guildId)} ·{" "}
                    {when(locale, row.at)}
                  </div>
                </div>
              </div>
            ))
          )}

          {skip > 0 || rows.data.length === TAKE ? (
            <div className="pager">
              {skip > 0 ? (
                <Link className="btn" href={`/bot/audit?skip=${Math.max(skip - TAKE, 0)}`}>
                  {t("nav.prev")}
                </Link>
              ) : null}
              {rows.data.length === TAKE ? (
                <Link className="btn" href={`/bot/audit?skip=${skip + TAKE}`}>
                  {t("audit.more")}
                </Link>
              ) : null}
            </div>
          ) : null}
        </section>
      ) : (
        <Fail failure={rows.failure} t={t} />
      )}
    </Frame>
  );
}

import Link from "next/link";

import { apiGet } from "../../../lib/api";
import { when } from "../../../lib/format";
import { currentLocale, pageTitle } from "../../../lib/locale";
import { panel, type Query } from "../../../lib/panel";

import { Fail } from "../../components/Fail";
import { Frame, PageHead } from "../../components/Frame";

type Audit = {
  auditId: number;
  userId: string;
  /** Null when the account is in no server Sonarr is in — the row then shows the id. */
  userName: string | null;
  guildId: string | null;
  /** Null for a bot-wide row, and for a server she has since been removed from. */
  guildName: string | null;
  action: string;
  target: string | null;
  /**
   * A jsonb column, so an object and never a string — the writers put `{value}`, `{enabled}` and a
   * per-area count map in here, and an empty `{}` when they pass no detail at all. Typing it as a
   * string made React throw "Objects are not valid as a React child" on every flag write.
   */
  detail: Record<string, unknown> | null;
  at: string;
}[];

/** `enabled: true` — the keys are already the field names an admin recognises. */
function describe(detail: Record<string, unknown> | null): string {
  return Object.entries(detail ?? {})
    .map(([key, value]) => `${key}: ${value === null ? "—" : String(value)}`)
    .join(" · ");
}

/** What the API returns per request when `take` is not given, and what this page asks for. */
const TAKE = 50;

export const generateMetadata = () => pageTitle("nav.audit");

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

  /**
   * The name of the server a row is about.
   *
   * Three sources in order, because each covers a case the next one misses: the API's own name (from
   * the gateway, so it covers every server she is in — not just the ones this admin is a member of),
   * then this admin's own guild list, then the raw id. The middle step is what keeps a row readable
   * for a server she has since been removed from but the admin is still in.
   */
  const serverName = (row: Audit[number]) => {
    if (row.guildId === null) {
      return t("audit.botWide");
    }

    return (
      row.guildName ??
      session.guilds.find((g) => g.guildId === row.guildId)?.name ??
      row.guildId
    );
  };

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
                  {describe(row.detail) ? (
                    <div className="row-sub">{describe(row.detail)}</div>
                  ) : null}
                  {/* Not `mono` on the whole line any more: it is mostly names now, and only the
                      ids that survive a failed lookup keep the typewriter face. */}
                  <div className="row-sub">
                    {t("audit.who")}{" "}
                    {row.userName === null ? (
                      <span className="mono">{row.userId}</span>
                    ) : (
                      row.userName
                    )}
                    {" · "}
                    {t("audit.server")} {serverName(row)} · {when(locale, row.at)}
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

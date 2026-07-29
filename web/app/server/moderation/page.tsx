import Link from "next/link";

import { apiGet } from "../../../lib/api";
import { when } from "../../../lib/format";
import { currentLocale, pageTitle } from "../../../lib/locale";
import { panel, type Query } from "../../../lib/panel";

import { Fail } from "../../components/Fail";
import { Frame, PageHead } from "../../components/Frame";
import { CaseFilter } from "./CaseFilter";

type Cases = {
  page: number;
  pageCount: number;
  totalCount: number;
  cases: {
    caseId: number;
    targetId: string;
    /** Null when the member has left and is in no other server Sonarr shares. */
    targetName: string | null;
    actorId: string;
    actorName: string | null;
    action: string;
    reason: string | null;
    expiresAt: string | null;
    createdAt: string;
  }[];
};

export const generateMetadata = () => pageTitle("nav.moderation");

/**
 * One person in a case row: their name, with the id kept as a tooltip-free second line only when
 * there is no name to show.
 *
 * The id is not printed beside a known name. A case row already carries an action, a reason, two
 * people and two timestamps; adding eighteen digits after each name is what made this page unreadable
 * in the first place. The id is still how the row is filtered — the filter box takes it — and it is
 * still in the API response for anyone reading that.
 */
function Who({ id, name }: { id: string; name: string | null }) {
  return name === null ? <span className="mono">{id}</span> : <span>{name}</span>;
}

/**
 * The moderation record for one server. Read-only: actions are taken on Discord, where the person
 * being actioned can see it happen, and this page is the log rather than a second set of controls.
 */
export default async function ModerationPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("server/moderation", searchParams);
  const locale = await currentLocale();
  const params = await searchParams;

  const id = guild!.guildId;
  const target = typeof params.target === "string" ? params.target : "";
  const page = Number.parseInt(typeof params.page === "string" ? params.page : "1", 10);
  const at = Number.isFinite(page) && page > 0 ? page : 1;

  const query = new URLSearchParams({ page: String(at) });
  if (target) {
    query.set("target", target);
  }

  const cases = await apiGet<Cases>(`/api/admin/cases/${id}?${query.toString()}`);

  /** A link to another page of the same filter, keeping the guild and the target. */
  const pageHref = (n: number) => {
    const q = new URLSearchParams({ guild: id, page: String(n) });
    if (target) {
      q.set("target", target);
    }

    return `/server/moderation?${q.toString()}`;
  };

  return (
    <Frame session={session} current="server/moderation" guild={guild} t={t}>
      <PageHead
        label={t("tier.guild")}
        title={t("moderation.title")}
        lead={t("moderation.lead", { guild: guild!.name })}
      />

      <CaseFilter
        guildId={id}
        target={target}
        labels={{
          label: t("moderation.filter"),
          hint: t("moderation.filterHint"),
          apply: t("moderation.apply"),
          clear: t("moderation.clear"),
        }}
      />

      {cases.ok ? (
        <section className="card">
          <div className="card-head">
            <h2>{t("moderation.title")}</h2>
            <span className="card-note mono">
              {t("moderation.page", { n: cases.data.page, total: cases.data.pageCount })}
            </span>
          </div>

          {cases.data.cases.length === 0 ? (
            <p className="empty">{t("moderation.empty")}</p>
          ) : (
            cases.data.cases.map((c) => (
              <div className="row" key={c.caseId}>
                <div className="row-main">
                  <div className="row-title">
                    {c.action} · {t("moderation.case", { id: c.caseId })}
                  </div>
                  <div className="row-sub">{c.reason ?? t("moderation.noReason")}</div>
                  {/* Not `mono` any more: this line is mostly names now, and a name set in a
                      typewriter face reads as data rather than as a person. The ids that remain
                      when a name is unknown carry `mono` themselves. */}
                  <div className="row-sub">
                    {t("moderation.target")} <Who id={c.targetId} name={c.targetName} />
                    {" · "}
                    {t("moderation.by")} <Who id={c.actorId} name={c.actorName} />
                    {" · "}
                    {when(locale, c.createdAt)} ·{" "}
                    {c.expiresAt
                      ? t("moderation.expires", { when: when(locale, c.expiresAt) })
                      : t("moderation.permanent")}
                  </div>
                </div>
              </div>
            ))
          )}

          {cases.data.pageCount > 1 ? (
            <div className="pager">
              {at > 1 ? (
                <Link className="btn" href={pageHref(at - 1)}>
                  {t("nav.prev")}
                </Link>
              ) : null}
              {at < cases.data.pageCount ? (
                <Link className="btn" href={pageHref(at + 1)}>
                  {t("nav.next")}
                </Link>
              ) : null}
            </div>
          ) : null}
        </section>
      ) : (
        <Fail failure={cases.failure} t={t} />
      )}
    </Frame>
  );
}

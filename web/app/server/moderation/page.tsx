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
    actorId: string;
    action: string;
    reason: string | null;
    expiresAt: string | null;
    createdAt: string;
  }[];
};

export const generateMetadata = () => pageTitle("nav.moderation");

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
                  <div className="row-sub mono">
                    {t("moderation.target")} {c.targetId} · {t("moderation.by")} {c.actorId} ·{" "}
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

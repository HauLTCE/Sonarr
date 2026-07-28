import Link from "next/link";

import { apiGet } from "@/lib/api";
import { when } from "@/lib/format";
import { guildScope, one, type SearchParams } from "@/lib/guilds";
import { translator } from "@/lib/strings";

import { Failure } from "../../../components/Failure";
import { GuildPicker } from "../../../components/GuildPicker";
import { NoGuilds } from "../../../components/NoGuilds";

type Cases = {
  page: number;
  pageCount: number;
  totalCount: number;
  cases: {
    caseId: number;
    targetId: string;
    actorId: string;
    action: string;
    reason: string;
    expiresAt: string | null;
    createdAt: string;
  }[];
};

/**
 * The mod log, paged, filterable by member id (checklist 298).
 *
 * Ids rather than names: the panel has no member cache, and looking every id up through the gateway
 * to render a page of ten rows would be a lot of calls for a label. The ids are what a moderator
 * pastes into `/modlog` anyway.
 *
 * The filter is a member id, which is a snowflake the moderator already has. It is passed straight
 * through — the API parses it and ignores anything that is not a number, so a typed word narrows
 * nothing rather than erroring.
 */
export default async function AdminCasesPage({ searchParams }: { searchParams: SearchParams }) {
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
  const params = await searchParams;

  const target = (one(params.target) ?? "").trim();
  const page = Math.max(1, Number.parseInt(one(params.page) ?? "1", 10) || 1);

  const query = new URLSearchParams({ page: String(page) });
  if (target.length > 0) {
    query.set("target", target);
  }

  const result = await apiGet<Cases>(`/api/admin/cases/${guild.guildId}?${query}`);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  const data = result.data;
  const pageHref = (next: number) => {
    const link = new URLSearchParams({ guild: guild.guildId, page: String(next) });
    if (target.length > 0) {
      link.set("target", target);
    }

    return `/admin/cases?${link}`;
  };

  return (
    <>
      <h1>{t("admin.cases.title")}</h1>

      <GuildPicker locale={locale} path="/admin/cases" options={options} selected={guild.guildId} />

      {/* A GET form so a filtered view is a URL someone can send to another mod. */}
      <form action="/admin/cases" method="get" className="card">
        <input type="hidden" name="guild" value={guild.guildId} />
        <div className="row">
          <label htmlFor="target">
            {t("admin.cases.filterLabel")}
            <input
              id="target"
              name="target"
              type="text"
              defaultValue={target}
              inputMode="numeric"
              autoComplete="off"
            />
          </label>
          <button type="submit" className="quiet">
            {t("admin.cases.filter")}
          </button>
        </div>
      </form>

      <section className="card">
        {data.cases.length === 0 ? (
          <p className="empty">{t("admin.cases.empty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <caption>{t("admin.cases.total", { count: data.totalCount })}</caption>
              <thead>
                <tr>
                  <th scope="col">{t("admin.cases.case")}</th>
                  <th scope="col">{t("admin.cases.action")}</th>
                  <th scope="col">{t("admin.cases.target")}</th>
                  <th scope="col">{t("admin.cases.actor")}</th>
                  <th scope="col">{t("admin.cases.reason")}</th>
                  <th scope="col">{t("common.when")}</th>
                  <th scope="col">{t("admin.cases.expires")}</th>
                </tr>
              </thead>
              <tbody>
                {data.cases.map((c) => (
                  <tr key={c.caseId}>
                    <th scope="row" className="num">
                      #{c.caseId}
                    </th>
                    <td>{c.action}</td>
                    <td className="mono">{c.targetId}</td>
                    <td className="mono">{c.actorId}</td>
                    <td>{c.reason}</td>
                    <td>{when(locale, c.createdAt)}</td>
                    <td>{c.expiresAt ? when(locale, c.expiresAt) : t("common.never")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <nav className="row" aria-label={t("admin.cases.pagination")}>
          {data.page > 1 && <Link href={pageHref(data.page - 1)}>{t("common.previous")}</Link>}
          <span className="hint">
            {t("common.page", { page: data.page, pageCount: Math.max(data.pageCount, 1) })}
          </span>
          {data.page < data.pageCount && (
            <Link href={pageHref(data.page + 1)}>{t("common.next")}</Link>
          )}
        </nav>
      </section>
    </>
  );
}

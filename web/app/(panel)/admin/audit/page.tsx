import Link from "next/link";

import { apiGet } from "@/lib/api";
import { when } from "@/lib/format";
import { one, type SearchParams } from "@/lib/guilds";
import { currentLocale } from "@/lib/locale";
import { translator } from "@/lib/strings";

import { Failure } from "../../../components/Failure";

type AuditRow = {
  auditId: number;
  userId: string;
  guildId: string | null;
  action: string;
  target: string | null;
  detail: unknown;
  at: string;
};

const pageSize = 50;

/**
 * Who did what through the panel (checklist 300).
 *
 * Not guild-scoped: the audit trail includes actions that are not tied to a server (a login, a
 * self-delete), and splitting it per guild would hide exactly those. Paging is skip/take against the
 * API rather than a page count — the endpoint returns rows, not a total, and "is there a next page"
 * is answerable from whether this one came back full.
 */
export default async function AdminAuditPage({ searchParams }: { searchParams: SearchParams }) {
  const [locale, params] = await Promise.all([currentLocale(), searchParams]);
  const t = translator(locale);

  const page = Math.max(1, Number.parseInt(one(params.page) ?? "1", 10) || 1);
  const skip = (page - 1) * pageSize;

  const result = await apiGet<AuditRow[]>(`/api/admin/audit?skip=${skip}&take=${pageSize}`);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  const rows = result.data;

  return (
    <>
      <h1>{t("admin.audit.title")}</h1>

      <section className="card">
        {rows.length === 0 ? (
          <p className="empty">{t("admin.audit.empty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("admin.audit.when")}</th>
                  <th scope="col">{t("admin.audit.who")}</th>
                  <th scope="col">{t("admin.audit.what")}</th>
                  <th scope="col">{t("admin.audit.target")}</th>
                  <th scope="col">{t("guild.label")}</th>
                  <th scope="col">{t("admin.audit.detail")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.auditId}>
                    <td>{when(locale, row.at)}</td>
                    <td className="mono">{row.userId}</td>
                    <td className="mono">{row.action}</td>
                    <td>{row.target ?? "—"}</td>
                    <td className="mono">{row.guildId ?? "—"}</td>
                    <td className="hint mono">{detail(row.detail)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <nav className="row" aria-label={t("admin.audit.pagination")}>
          {page > 1 && <Link href={`/admin/audit?page=${page - 1}`}>{t("common.previous")}</Link>}
          <span className="hint">{t("admin.audit.pageNumber", { page })}</span>
          {/* A full page means there is probably another; the endpoint returns no total. */}
          {rows.length === pageSize && (
            <Link href={`/admin/audit?page=${page + 1}`}>{t("common.next")}</Link>
          )}
        </nav>
      </section>

      <p className="hint">{t("admin.audit.note")}</p>
    </>
  );
}

/**
 * The jsonb detail column, as one short line.
 *
 * Rendered as text, never as markup: the column holds whatever the action recorded, and an audit
 * row is the last place that should be able to inject anything into the page.
 */
function detail(value: unknown): string {
  if (value === null || value === undefined) {
    return "—";
  }

  const text = typeof value === "string" ? value : JSON.stringify(value);

  return text.length > 120 ? `${text.slice(0, 120)}…` : text;
}

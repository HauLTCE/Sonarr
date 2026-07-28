import { apiGet } from "../../lib/api";
import { when } from "../../lib/format";
import { currentLocale } from "../../lib/locale";
import { panel, type Query } from "../../lib/panel";

import { Fail } from "../components/Fail";
import { Frame, PageHead } from "../components/Frame";

type UserError = { caseId: string; command: string; message: string; at: string };

/**
 * Your own failed commands. Not guild-scoped and not bot-wide: the API keys this list to the
 * session's user id, so what you see here is only ever yours.
 */
export default async function ErrorsPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("errors", searchParams);
  const locale = await currentLocale();

  const errors = await apiGet<UserError[]>("/api/me/errors");

  return (
    <Frame session={session} current="errors" guild={guild} t={t}>
      <PageHead label={t("tier.user")} title={t("errors.title")} lead={t("errors.lead")} />

      {errors.ok ? (
        <section className="card">
          <div className="card-head">
            <h2>{t("errors.title")}</h2>
          </div>

          {errors.data.length === 0 ? (
            <p className="empty">{t("errors.empty")}</p>
          ) : (
            errors.data.map((e) => (
              <div className="row" key={e.caseId}>
                <div className="row-main">
                  <div className="row-title">{e.message}</div>
                  <div className="row-sub mono">
                    /{e.command} · {t("errors.case", { id: e.caseId })} · {when(locale, e.at)}
                  </div>
                </div>
              </div>
            ))
          )}

          <p className="hint">{t("errors.reset")}</p>
        </section>
      ) : (
        <Fail failure={errors.failure} t={t} />
      )}
    </Frame>
  );
}

import { apiGet } from "@/lib/api";
import { when } from "@/lib/format";
import { currentLocale } from "@/lib/locale";
import { translator } from "@/lib/strings";

import { Failure } from "../../components/Failure";

type UserError = { caseId: string; command: string; message: string; at: string };

/** The visitor's own recent command failures, newest first (checklist 292). */
export default async function ErrorsPage() {
  const [locale, result] = await Promise.all([
    currentLocale(),
    apiGet<UserError[]>("/api/me/errors"),
  ]);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  const t = translator(locale);

  return (
    <>
      <h1>{t("errors.title")}</h1>
      <p className="lead">{t("errors.lead")}</p>

      <section className="card">
        {result.data.length === 0 ? (
          <p className="empty">{t("errors.empty")}</p>
        ) : (
          <div className="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("common.when")}</th>
                  <th scope="col">{t("errors.command")}</th>
                  <th scope="col">{t("errors.message")}</th>
                  <th scope="col">{t("errors.reference")}</th>
                </tr>
              </thead>
              <tbody>
                {result.data.map((e) => (
                  <tr key={e.caseId}>
                    <td>{when(locale, e.at)}</td>
                    <td className="mono">{e.command}</td>
                    <td>{e.message}</td>
                    <td className="mono">{e.caseId}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <p className="hint">{t("errors.note")}</p>
    </>
  );
}

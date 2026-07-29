import { redirect } from "next/navigation";

import { apiGet } from "../../lib/api";
import { pageTitle, serverTranslator } from "../../lib/locale";

import { LoginForm } from "./LoginForm";

type Me = { userId: string };

/**
 * The only page outside the frame — no navbar here, because there is nowhere to go yet.
 *
 * A live session skips straight through: arriving at /login while logged in is almost always a stale
 * tab or a bookmark, and showing a login form to someone already logged in is a dead end.
 */
export const generateMetadata = () => pageTitle("login.title");

export default async function LoginPage() {
  const t = await serverTranslator();

  const me = await apiGet<Me>("/api/me");
  if (me.ok) {
    redirect("/");
  }

  return (
    // <main>, not a div: this page is outside Frame, so nothing else on it is a landmark. A screen
    // reader user jumping by landmark would otherwise find none at all here.
    <main className="solo">
      <div className="sheet">
        <div className="page-head">
          <div className="page-label mono">{t("app.name")}</div>
          <h1>{t("login.title")}</h1>
          <p className="page-lead">{t("login.lead")}</p>
        </div>

        <section className="card">
          <LoginForm
            labels={{
              handle: t("login.handle"),
              handleHint: t("login.handleHint"),
              send: t("login.send"),
              sending: t("login.sending"),
              code: t("login.code"),
              codeHint: t("login.codeHint"),
              verify: t("login.verify"),
              verifying: t("login.verifying"),
              remember: t("login.remember"),
              sent: t("login.sent"),
              needHandle: t("login.needHandle"),
              badCode: t("login.badCode"),
              tooMany: t("login.tooMany"),
              rateLimited: t("login.rateLimited"),
              failed: t("login.failed"),
              restart: t("login.restart"),
            }}
          />
        </section>

        {/* The one failure this flow cannot report, because the API deliberately cannot tell us
            whether the DM landed. So it is answered before it is asked. */}
        <section className="card">
          <div className="card-head">
            <h2>{t("login.noDmTitle")}</h2>
          </div>
          <p className="hint">{t("login.noDm")}</p>
        </section>
      </div>
    </main>
  );
}

import { redirect } from "next/navigation";

import { hasSession } from "@/lib/api";
import { currentLocale } from "@/lib/locale";
import { translator } from "@/lib/strings";

import { LoginForm } from "./LoginForm";

/**
 * The only page outside the shell — there is no nav to show someone who is not logged in yet.
 */
export default async function LoginPage() {
  const locale = await currentLocale();
  const t = translator(locale);

  // Already carrying a cookie: the panel decides whether it is still valid, not this page.
  if (await hasSession()) {
    redirect("/");
  }

  return (
    <div className="shell">
      <a className="skip" href="#main">
        {t("nav.skipToContent")}
      </a>

      <header className="topbar">
        <span className="brand">{t("app.name")}</span>
      </header>

      <main id="main">
        <div className="card">
          <h1>{t("login.title")}</h1>
          <p className="lead">{t("login.lead")}</p>

          <LoginForm locale={locale} />
        </div>

        {/* docs/09 asks for this explicitly: the commonest failure is closed DMs, and the flow
            cannot report it without leaking who has an account. */}
        <div className="card">
          <h2>{t("login.dmsClosedTitle")}</h2>
          <p className="lead">{t("login.dmsClosed")}</p>
        </div>
      </main>

      <footer className="footer">{t("app.tagline")}</footer>
    </div>
  );
}

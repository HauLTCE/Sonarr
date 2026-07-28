import { pageTitle, serverTranslator } from "../../lib/locale";

import { LogoutButton } from "./LogoutButton";

/**
 * A confirmation step rather than a route that logs you out on arrival.
 *
 * The header's Log out is a link, and a link that destroys state the moment it is prefetched or
 * mis-clicked is a trap — so this page asks, and says what logging out does and does not touch.
 */
export const generateMetadata = () => pageTitle("logout.title");

export default async function LogoutPage() {
  const t = await serverTranslator();

  return (
    // <main>, not a div: this page is outside Frame, so nothing else on it is a landmark. A screen
    // reader user jumping by landmark would otherwise find none at all here.
    <main className="solo">
      <div className="sheet slide">
        <div className="page-head">
          <div className="page-label mono">{t("app.name")}</div>
          <h1>{t("logout.title")}</h1>
          <p className="page-lead">{t("logout.lead")}</p>
        </div>

        <section className="card">
          <LogoutButton
            labels={{
              confirm: t("logout.confirm"),
              cancel: t("logout.cancel"),
              working: t("logout.working"),
              failed: t("logout.failed"),
            }}
          />
        </section>
      </div>
    </main>
  );
}

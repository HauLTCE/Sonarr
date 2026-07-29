import Link from "next/link";

import { serverTranslator } from "../lib/locale";

/**
 * Outside the frame on purpose: a 404 can be reached logged out, and the rail is built from a
 * session. One way out, and it is the panel rather than the browser's Back.
 */
export default async function NotFound() {
  const t = await serverTranslator();

  return (
    <div className="solo">
      <div className="sheet">
        <div className="page-head">
          <div className="page-label mono">{t("app.name")}</div>
          <h1>{t("state.notFound")}</h1>
        </div>

        <section className="card">
          <div className="actions">
            <Link className="btn btn-accent" href="/">
              {t("state.goHome")}
            </Link>
          </div>
        </section>
      </div>
    </div>
  );
}

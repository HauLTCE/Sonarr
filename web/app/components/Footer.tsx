import Link from "next/link";

import { when } from "../../lib/format";
import type { Session } from "../../lib/pages";
import type { Locale, Translate } from "../../lib/strings";

/**
 * The footer: who you are signed in as, when that stops being true, and the privacy page.
 *
 * It holds what the rail's counter used to hold — the answer to "where am I" — except the honest
 * version. "3 of 12" told a visitor their position in a list they never asked to be in; the session's
 * expiry is the thing they will actually want to know before they start typing into a form.
 */
export function Footer({
  session,
  locale,
  t,
}: {
  session: Session;
  locale: Locale;
  t: Translate;
}) {
  return (
    <footer className="footer">
      <div className="footer-inner">
        <p className="footer-line">
          {t("foot.session", { user: session.userId })}
          {" · "}
          <span className="footer-dim">
            {session.expiresAt === null
              ? t("foot.expiresNever")
              : t("foot.expires", { when: when(locale, session.expiresAt) })}
          </span>
        </p>

        <p className="footer-line footer-dim">
          {t("foot.legal")}{" "}
          <Link href="/privacy">{t("foot.privacy")}</Link>
        </p>
      </div>
    </footer>
  );
}

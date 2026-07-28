import { currentLocale } from "@/lib/locale";
import { translator } from "@/lib/strings";

import { DangerButton } from "./DangerButton";
import { LogoutEverywhere } from "./LogoutEverywhere";

/**
 * Export and the two deletions (checklist 294).
 *
 * Each destructive action gets its own card with its own consequences spelled out before its button.
 * "Delete everything" says what it keeps as well as what it removes — moderation cases stay, because
 * they are the server's record rather than the member's (docs/06).
 */
export default async function PrivacyPage() {
  const locale = await currentLocale();
  const t = translator(locale);

  return (
    <>
      <h1>{t("privacy.title")}</h1>
      <p className="lead">{t("privacy.lead")}</p>

      <section className="card">
        <h2>{t("privacy.export")}</h2>
        <p>{t("privacy.exportLead")}</p>
        <p>
          {/* A plain link: the API answers with a file, which is exactly what a browser download is. */}
          <a href="/api/me/export">{t("privacy.exportButton")}</a>
        </p>
      </section>

      <section className="card">
        <h2>{t("privacy.deleteChat")}</h2>
        <p>{t("privacy.deleteChatLead")}</p>
        <DangerButton locale={locale} path="/api/me/chat-memory" />
      </section>

      <section className="card">
        <h2>{t("privacy.deleteAll")}</h2>
        <p>{t("privacy.deleteAllLead")}</p>
        <DangerButton locale={locale} path="/api/me/everything" />
      </section>

      <section className="card">
        <h2>{t("privacy.sessions")}</h2>
        <p>{t("privacy.sessionsLead")}</p>
        <LogoutEverywhere locale={locale} />
      </section>
    </>
  );
}

import { panel, type Query } from "../../lib/panel";

import { Frame, PageHead } from "../components/Frame";
import { DangerButtons } from "./DangerButtons";

/**
 * Export and delete. The only page whose buttons cannot be undone, so every one of them says what
 * it removes and what it leaves before it is pressed, and the two deletes need the word typed.
 */
export default async function PrivacyPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("privacy", searchParams);

  return (
    <Frame session={session} current="privacy" guild={guild} t={t}>
      <PageHead label={t("tier.user")} title={t("privacy.title")} lead={t("privacy.lead")} />

      <section className="card">
        <div className="card-head">
          <h2>{t("privacy.export")}</h2>
        </div>
        {/* A plain link: the API answers with a file, so the browser's own download is the whole
            feature and there is nothing for JavaScript to add. */}
        <a className="btn" href="/api/me/export" download>
          {t("privacy.export")}
        </a>
        <p className="hint">{t("privacy.exportHint")}</p>
      </section>

      <DangerButtons
        labels={{
          forgetChat: t("privacy.forgetChat"),
          forgetChatHint: t("privacy.forgetChatHint"),
          forgetAll: t("privacy.forgetAll"),
          forgetAllHint: t("privacy.forgetAllHint"),
          confirmLabel: t("privacy.confirmLabel"),
          confirmHint: t("privacy.confirmHint"),
          confirmWrong: t("privacy.confirmWrong"),
          logoutAll: t("privacy.logoutAll"),
          logoutAllHint: t("privacy.logoutAllHint"),
          working: t("privacy.working"),
          failed: t("privacy.failed"),
          backupNote: t("privacy.backupNote"),
          // A function rather than a string: the counts are only known after the delete returns,
          // and the client component has no dictionary to interpolate with.
          deleted: (n, total) => t("privacy.deleted", { n, total }),
        }}
      />
    </Frame>
  );
}

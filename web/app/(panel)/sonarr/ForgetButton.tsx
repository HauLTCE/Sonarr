"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "@/lib/client";
import type { Locale } from "@/lib/strings";
import { translator } from "@/lib/strings";

/**
 * "Ask her to forget this" for one fact (checklist 291).
 *
 * A button rather than a form post because the API answers with her own line about forgetting, and
 * that line is worth showing. On success the router refresh re-reads the page, so the row it removed
 * disappears without this component tracking a list.
 */
export function ForgetButton({
  locale,
  guildId,
  predicate,
}: {
  locale: Locale;
  guildId: string;
  predicate: string;
}) {
  const t = translator(locale);
  const router = useRouter();
  const [state, setState] = useState<"idle" | "busy" | "failed">("idle");

  return (
    <>
      <button
        type="button"
        className="quiet"
        disabled={state === "busy"}
        onClick={async () => {
          setState("busy");

          const result = await write(
            // The predicate is a path segment and can contain anything she wrote down.
            `/api/me/facts/${encodeURIComponent(predicate)}?guildId=${guildId}`,
            "DELETE",
          );

          if (result.ok) {
            router.refresh();
            return;
          }

          setState("failed");
        }}
      >
        {state === "busy" ? t("sonarr.forgetting") : t("sonarr.forget")}
      </button>

      {state === "failed" && (
        <p className="hint" role="alert">
          {t("sonarr.forgetFailed")}
        </p>
      )}
    </>
  );
}

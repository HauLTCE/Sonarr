import { redirect } from "next/navigation";

import type { ApiFailure } from "@/lib/api";
import type { Locale } from "@/lib/strings";
import { translator } from "@/lib/strings";

/**
 * What a page renders when a read did not come back.
 *
 * `unauthorized` never renders: an expired session means the visitor belongs on /login, and showing
 * them an empty panel with a "log in again" note would be a worse version of the same thing.
 */
export function Failure({ locale, failure }: { locale: Locale; failure: ApiFailure }) {
  if (failure === "unauthorized") {
    redirect("/login");
  }

  const t = translator(locale);

  return (
    <p className="notice bad" role="alert">
      {t(failure === "forbidden" ? "common.forbidden" : "common.error")}
    </p>
  );
}

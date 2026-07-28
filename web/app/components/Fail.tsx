import { redirect } from "next/navigation";

import type { ApiFailure } from "../../lib/api";
import type { Translate } from "../../lib/strings";

/**
 * What a page shows in place of a section it could not load.
 *
 * `unauthorized` never renders: an expired session is not a message, it is a login. The other two
 * say what happened and what the visitor can do, because "Could not load that" on its own leaves
 * someone refreshing a page that will never work.
 */
export function Fail({ failure, t }: { failure: ApiFailure; t: Translate }) {
  if (failure === "unauthorized") {
    redirect("/login");
  }

  return (
    <p className={failure === "forbidden" ? "notice notice-warn" : "notice notice-danger"}>
      {t(failure === "forbidden" ? "state.forbidden" : "state.error")}
    </p>
  );
}

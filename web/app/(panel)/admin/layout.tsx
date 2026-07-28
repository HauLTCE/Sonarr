import { me } from "@/lib/guilds";
import { currentLocale } from "@/lib/locale";
import { translator } from "@/lib/strings";

import { Failure } from "../../components/Failure";

/**
 * The admin gate for every page under /admin.
 *
 * The API already refuses a non-admin on each `/api/admin/*` route, so the pages that read through
 * it were never leaking data. The status page is the exception — it reads the unauthenticated
 * `/api/status` directly — and "the nav does not show the link" is not a gate for anyone who can
 * type a URL. One layout covers all six routes, so a new admin page is gated by existing.
 */
export default async function AdminLayout({ children }: { children: React.ReactNode }) {
  const [locale, session] = await Promise.all([currentLocale(), me()]);

  // The (panel) layout above has already redirected anyone without a session, so a failure here is
  // a race (session revoked mid-render) and Failure sends them to /login the same way.
  if (!session.ok) {
    return <Failure locale={locale} failure={session.failure} />;
  }

  if (!session.data.isAdmin) {
    return (
      <p className="notice bad" role="alert">
        {translator(locale)("common.forbidden")}
      </p>
    );
  }

  return <>{children}</>;
}

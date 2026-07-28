import { redirect } from "next/navigation";

import { me } from "@/lib/guilds";
import { currentLocale } from "@/lib/locale";

import { Shell } from "../components/Shell";

/**
 * The frame for every logged-in page, and the one place the session is checked.
 *
 * `/api/me` rather than "is a cookie present": the cookie could be expired or revoked, and a layout
 * that trusts its existence would render a shell full of failed reads. The admin nav is gated on the
 * same answer, so it is the API deciding who is an admin, not the browser.
 */
export default async function PanelLayout({ children }: { children: React.ReactNode }) {
  const [locale, session] = await Promise.all([currentLocale(), me()]);

  if (!session.ok) {
    // Even a hard API failure sends the visitor to /login: without knowing who they are there is
    // no panel to draw, and the login page works whether or not the API is up.
    redirect("/login");
  }

  return (
    <Shell locale={locale} isAdmin={session.data.isAdmin}>
      {children}
    </Shell>
  );
}

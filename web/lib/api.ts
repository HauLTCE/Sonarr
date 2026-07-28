import { cookies } from "next/headers";

/**
 * Reads from the bot's API for server components (docs/09: data pages are rendered on the server,
 * so the J2900 sends HTML rather than a SPA bundle).
 *
 * Server-side fetches go straight to Kestrel rather than back through this app's own `/api` rewrite
 * — the rewrite exists for the browser. The session cookie is HttpOnly, so it has to be forwarded
 * by hand here; nothing else from the incoming request is passed on.
 */
const base = process.env.SONARR_API_URL ?? "http://127.0.0.1:5088";

export const sessionCookie = "sonarr_session";

export const csrfCookie = "sonarr_csrf";

export const csrfHeader = "X-CSRF-Token";

/** What a page does about a failed read. `unauthorized` sends the visitor to /login. */
export type ApiFailure = "unauthorized" | "forbidden" | "error";

export type ApiResult<T> = { ok: true; data: T } | { ok: false; failure: ApiFailure };

/**
 * One GET against the API, with the caller's session attached.
 *
 * Never throws: a panel page that renders "could not load that" is better than a 500, and every
 * caller already has to handle the logged-out case.
 */
export async function apiGet<T>(path: string): Promise<ApiResult<T>> {
  const store = await cookies();
  const session = store.get(sessionCookie)?.value;

  if (!session) {
    return { ok: false, failure: "unauthorized" };
  }

  let response: Response;

  try {
    response = await fetch(`${base}${path}`, {
      headers: {
        // Encoded because a cookie value reaches us as-is; the session id is hex, but a header
        // built from unvalidated input is how a header-injection bug starts.
        cookie: `${sessionCookie}=${encodeURIComponent(session)}`,
        accept: "application/json",
      },
      // These pages are "what is true right now" — a cached level or a cached case list is a bug.
      cache: "no-store",
    });
  } catch {
    return { ok: false, failure: "error" };
  }

  if (response.status === 401) {
    return { ok: false, failure: "unauthorized" };
  }

  if (response.status === 403) {
    return { ok: false, failure: "forbidden" };
  }

  if (!response.ok) {
    return { ok: false, failure: "error" };
  }

  try {
    return { ok: true, data: (await response.json()) as T };
  } catch {
    return { ok: false, failure: "error" };
  }
}

/** True when a session cookie is present at all — the layout's cheap "are we logged in" check. */
export async function hasSession(): Promise<boolean> {
  const store = await cookies();

  return Boolean(store.get(sessionCookie)?.value);
}

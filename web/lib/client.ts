/**
 * Writes from the browser, with the CSRF header the API insists on.
 *
 * Every mutation goes through here so there is one place that reads the token, and one shape of
 * failure for the pages to render. Requests go to the same-origin `/api/*` rewrite, so the session
 * cookie rides along without any CORS or credentials dance.
 */

/** The double-submit token. Deliberately not HttpOnly on the API side — this is what reads it. */
function csrf(): string {
  const match = document.cookie.match(/(?:^|;\s*)sonarr_csrf=([^;]*)/);

  return match ? decodeURIComponent(match[1]) : "";
}

/**
 * `error` is the API's own message when it sent one. Config and flag writes validate in the same
 * Application service the slash commands use, and its reason ("xp_multiplier must be 1-5") is worth
 * far more to the admin than a generic failure — so it comes back to the caller rather than being
 * swallowed here.
 */
export type WriteResult<T> =
  | { ok: true; data: T }
  | { ok: false; status: number; error: string | null };

export async function write<T>(
  path: string,
  method: "POST" | "PUT" | "DELETE",
  body?: unknown,
): Promise<WriteResult<T>> {
  let response: Response;

  try {
    response = await fetch(path, {
      method,
      headers: {
        "X-CSRF-Token": csrf(),
        ...(body === undefined ? {} : { "content-type": "application/json" }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    // 0 means "never reached the server", which reads the same as any other failure to the caller.
    return { ok: false, status: 0, error: null };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: await failureText(response) };
  }

  if (response.status === 204) {
    return { ok: true, data: undefined as T };
  }

  try {
    return { ok: true, data: (await response.json()) as T };
  } catch {
    return { ok: false, status: response.status, error: null };
  }
}

/**
 * The `{ error }` field from a failed response, if there is one.
 *
 * Only a string is accepted, and only from our own API: rendering an arbitrary JSON value as an
 * error message is how a page ends up showing "[object Object]" on a bad day.
 */
async function failureText(response: Response): Promise<string | null> {
  try {
    const body: unknown = await response.json();

    if (body && typeof body === "object" && "error" in body) {
      const error = (body as { error: unknown }).error;

      return typeof error === "string" && error.trim().length > 0 ? error : null;
    }
  } catch {
    // A non-JSON error body (a proxy's HTML 502, say) is not a message worth showing.
  }

  return null;
}

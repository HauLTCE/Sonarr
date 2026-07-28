"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { type Locale, translator } from "@/lib/strings";

type Stage = "handle" | "code";

/**
 * The two-step DM-token flow (docs/09). A client component because it is the one place in the panel
 * that has to POST and react to the answer without a page of its own per step.
 *
 * The failure wording deliberately does not distinguish "no such account" from "DMs closed" — the
 * API returns the same 202 either way, and a page that guessed would undo that (docs/09: no user
 * enumeration). The help box covers both cases at once.
 */
export function LoginForm({ locale }: { locale: Locale }) {
  // A function is not serializable across the server/client boundary, so client components take the
  // locale and bind their own `t` — the dictionary is a module import either way.
  const t = translator(locale);
  const router = useRouter();
  const [stage, setStage] = useState<Stage>("handle");
  const [username, setUsername] = useState("");
  const [code, setCode] = useState("");
  const [remember, setRemember] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function requestCode(event: React.FormEvent) {
    event.preventDefault();

    // Trimmed and stripped here for the person typing; the API validates it again for real.
    const handle = username.trim().replace(/^@/, "");
    if (!handle) {
      setError(t("login.needHandle"));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = await fetch("/api/auth/request-token", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ username: handle }),
      });

      if (response.status === 429) {
        setError(t("login.rateLimited"));
        return;
      }

      // 202 is the only success, and it means the same thing whoever asked.
      setUsername(handle);
      setStage("code");
      setNotice(t("login.sent"));
    } catch {
      setError(t("login.failed"));
    } finally {
      setBusy(false);
    }
  }

  async function verify(event: React.FormEvent) {
    event.preventDefault();

    setBusy(true);
    setError(null);

    try {
      const response = await fetch("/api/auth/verify", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ username, code: code.trim(), remember }),
      });

      if (response.status === 429) {
        setError(t("login.tooManyAttempts"));
        setStage("handle");
        setCode("");
        return;
      }

      if (!response.ok) {
        setError(t("login.badCode"));
        setCode("");
        return;
      }

      // The session cookie is set by the API's response; refresh so the server components on the
      // next page read it.
      router.push("/");
      router.refresh();
    } catch {
      setError(t("login.failed"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      {/* aria-live so a screen reader hears the result of a submit without moving focus. */}
      <div aria-live="polite">
        {error && (
          <p className="notice bad" role="alert">
            {error}
          </p>
        )}
        {!error && notice && <p className="notice good">{notice}</p>}
      </div>

      {stage === "handle" ? (
        <form onSubmit={requestCode}>
          <label htmlFor="handle">
            {t("login.handleLabel")}
            <input
              id="handle"
              name="handle"
              type="text"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              maxLength={64}
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              aria-describedby="handle-hint"
            />
          </label>
          <p className="hint" id="handle-hint">
            {t("login.handleHint")}
          </p>

          <div className="row">
            <button type="submit" disabled={busy}>
              {busy ? t("login.sending") : t("login.sendCode")}
            </button>
          </div>
        </form>
      ) : (
        <form onSubmit={verify}>
          <label htmlFor="code">
            {t("login.codeLabel")}
            <input
              id="code"
              name="code"
              type="text"
              inputMode="text"
              autoComplete="one-time-code"
              autoCapitalize="characters"
              spellCheck={false}
              maxLength={16}
              value={code}
              onChange={(e) => setCode(e.target.value)}
              aria-describedby="code-hint"
            />
          </label>
          <p className="hint" id="code-hint">
            {t("login.codeHint")}
          </p>

          <label className="check" htmlFor="remember">
            <input
              id="remember"
              name="remember"
              type="checkbox"
              checked={remember}
              onChange={(e) => setRemember(e.target.checked)}
            />
            {t("login.remember")}
          </label>

          <div className="row">
            <button type="submit" disabled={busy || code.trim().length === 0}>
              {busy ? t("login.verifying") : t("login.verify")}
            </button>
            <button
              type="button"
              className="quiet"
              disabled={busy}
              onClick={() => {
                setStage("handle");
                setCode("");
                setNotice(null);
                setError(null);
              }}
            >
              {t("login.startOver")}
            </button>
          </div>
        </form>
      )}
    </>
  );
}

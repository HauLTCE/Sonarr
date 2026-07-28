"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "../../lib/client";

/** Every string this form can show, resolved on the server so no dictionary reaches the browser. */
export type LoginLabels = {
  handle: string;
  handleHint: string;
  send: string;
  sending: string;
  code: string;
  codeHint: string;
  verify: string;
  verifying: string;
  remember: string;
  sent: string;
  needHandle: string;
  badCode: string;
  expired: string;
  tooMany: string;
  rateLimited: string;
  failed: string;
  restart: string;
};

/**
 * The two-step DM login: ask for a code, then type it.
 *
 * Both steps stay on one page and the handle stays visible in step two, so the visitor can see what
 * they asked a code for. The API answers `request-token` identically whether or not the account is
 * known — so this form says "if that account is known here" rather than claiming a DM was sent.
 */
export function LoginForm({ labels }: { labels: LoginLabels }) {
  const router = useRouter();

  const [handle, setHandle] = useState("");
  const [code, setCode] = useState("");
  const [remember, setRemember] = useState(false);
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

  async function requestCode(event: React.FormEvent) {
    event.preventDefault();

    // Validated here so an empty submit does not cost a round trip; the API validates too.
    if (handle.trim().length === 0) {
      setProblem(labels.needHandle);

      return;
    }

    setBusy(true);
    setProblem(null);

    const result = await write("/api/auth/request-token", "POST", { username: handle.trim() });

    setBusy(false);

    if (result.ok) {
      setSent(true);

      return;
    }

    setProblem(result.status === 429 ? labels.rateLimited : labels.failed);
  }

  async function verify(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setProblem(null);

    const result = await write("/api/auth/verify", "POST", {
      username: handle.trim(),
      // Upper-cased because the alphabet is upper-case only and typing it in lower case is not a
      // wrong code — it is the same code.
      code: code.trim().toUpperCase(),
      remember,
    });

    if (result.ok) {
      // A full navigation rather than a push: the session cookie was just set and every page above
      // this one reads it on the server.
      router.replace("/");
      router.refresh();

      return;
    }

    setBusy(false);
    setProblem(
      result.status === 429
        ? labels.tooMany
        : result.status === 401
          ? labels.badCode
          : labels.failed,
    );
  }

  return sent ? (
    <form onSubmit={verify}>
      <p className="notice">{labels.sent}</p>

      <div className="field">
        <label htmlFor="code">{labels.code}</label>
        <input
          id="code"
          name="code"
          type="text"
          value={code}
          onChange={(e) => setCode(e.target.value)}
          // The code is not a secret to the person holding it, and hiding it causes typos.
          autoComplete="one-time-code"
          inputMode="text"
          autoCapitalize="characters"
          spellCheck={false}
          className="mono code-input"
          required
          aria-describedby="code-hint"
          autoFocus
        />
        <p className="hint" id="code-hint">
          {labels.codeHint}
        </p>
      </div>

      <div className="field">
        <label className="switch" htmlFor="remember">
          <input
            id="remember"
            type="checkbox"
            checked={remember}
            onChange={(e) => setRemember(e.target.checked)}
          />
          <span className="switch-track" aria-hidden="true" />
          <span>{labels.remember}</span>
        </label>
      </div>

      {problem ? (
        <p className="notice notice-danger" role="alert">
          {problem}
        </p>
      ) : null}

      <div className="actions">
        <button className="btn btn-accent" type="submit" disabled={busy}>
          {busy ? labels.verifying : labels.verify}
        </button>
        <button
          className="btn"
          type="button"
          onClick={() => {
            setSent(false);
            setCode("");
            setProblem(null);
          }}
        >
          {labels.restart}
        </button>
      </div>
    </form>
  ) : (
    <form onSubmit={requestCode}>
      <div className="field">
        <label htmlFor="handle">{labels.handle}</label>
        <input
          id="handle"
          name="username"
          type="text"
          value={handle}
          onChange={(e) => setHandle(e.target.value)}
          autoComplete="username"
          spellCheck={false}
          required
          aria-describedby="handle-hint"
          autoFocus
        />
        <p className="hint" id="handle-hint">
          {labels.handleHint}
        </p>
      </div>

      {problem ? (
        <p className="notice notice-danger" role="alert">
          {problem}
        </p>
      ) : null}

      <div className="actions">
        <button className="btn btn-accent" type="submit" disabled={busy}>
          {busy ? labels.sending : labels.send}
        </button>
      </div>
    </form>
  );
}

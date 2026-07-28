"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "../../lib/client";

/** What the API insists on in the body. Typing it is the step you cannot click through. */
const CONFIRM = "DELETE";

type Labels = {
  forgetChat: string;
  forgetChatHint: string;
  forgetAll: string;
  forgetAllHint: string;
  confirmLabel: string;
  confirmHint: string;
  confirmWrong: string;
  logoutAll: string;
  logoutAllHint: string;
  working: string;
  failed: string;
  backupNote: string;
  /** Takes the two counts — "23 of 23 rows removed" rather than a bare "23 / 23". */
  deleted: (n: number, total: number) => string;
};

type Done = { deleted: number; total: number; note: string };

/**
 * The three irreversible buttons.
 *
 * One confirmation field for both deletes rather than a modal each: the field is visible before
 * either button is pressed, so a visitor can see what the price of admission is without triggering
 * anything. Each button stays disabled until the word is exact — the API checks it too, so this is
 * the courtesy, not the lock.
 */
export function DangerButtons({ labels }: { labels: Labels }) {
  const router = useRouter();
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<Done | null>(null);

  const armed = confirm.trim() === CONFIRM;

  async function erase(path: string) {
    setBusy(path);
    setError(null);
    setDone(null);

    const result = await write<Done>(path, "DELETE", { confirm: CONFIRM });

    setBusy(null);

    if (!result.ok) {
      setError(result.error ?? labels.failed);
      return;
    }

    setConfirm("");
    setDone(result.data);
    // Every page reads the same rows, so the whole tree is stale now.
    router.refresh();
  }

  async function logoutEverywhere() {
    setBusy("logout");
    setError(null);

    const result = await write("/api/auth/logout-all", "POST");

    setBusy(null);

    if (!result.ok) {
      setError(result.error ?? labels.failed);
      return;
    }

    // This login is one of the ones just ended, so there is nothing to refresh back into.
    router.push("/login");
  }

  return (
    <>
      <section className="card">
        <div className="card-head">
          <h2>{labels.confirmLabel}</h2>
        </div>

        <label htmlFor="confirm">{labels.confirmLabel}</label>
        <input
          id="confirm"
          type="text"
          value={confirm}
          autoComplete="off"
          spellCheck={false}
          onChange={(event) => setConfirm(event.target.value)}
        />
        <p className="hint">{labels.confirmHint}</p>

        <div className="actions">
          <div className="action">
            <button
              type="button"
              className="btn btn-danger"
              disabled={!armed || busy !== null}
              onClick={() => erase("/api/me/chat-memory")}
            >
              {busy === "/api/me/chat-memory" ? labels.working : labels.forgetChat}
            </button>
            <p className="hint">{labels.forgetChatHint}</p>
          </div>

          <div className="action">
            <button
              type="button"
              className="btn btn-danger"
              disabled={!armed || busy !== null}
              onClick={() => erase("/api/me/everything")}
            >
              {busy === "/api/me/everything" ? labels.working : labels.forgetAll}
            </button>
            <p className="hint">{labels.forgetAllHint}</p>
          </div>
        </div>

        {!armed && confirm.length > 0 ? (
          <p className="notice notice-warn">{labels.confirmWrong}</p>
        ) : null}

        {error ? <p className="notice notice-danger">{error}</p> : null}

        {done ? (
          <p className="notice" role="status">
            {labels.deleted(done.deleted, done.total)} {done.note || labels.backupNote}
          </p>
        ) : null}
      </section>

      <section className="card">
        <div className="card-head">
          <h2>{labels.logoutAll}</h2>
        </div>
        <button
          type="button"
          className="btn"
          disabled={busy !== null}
          onClick={logoutEverywhere}
        >
          {busy === "logout" ? labels.working : labels.logoutAll}
        </button>
        <p className="hint">{labels.logoutAllHint}</p>
      </section>
    </>
  );
}

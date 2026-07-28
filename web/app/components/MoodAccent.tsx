"use client";

import { useEffect, useState } from "react";

/**
 * Her mood, as the page's accent colour.
 *
 * Each persona mode (persona/sonarr.yaml → modes) gets two shades. The page drifts between them
 * continuously via a CSS animation, so the accent is always moving without JS touching a colour
 * per frame; the mood only decides which pair it drifts between. Every shade below clears 4.9:1
 * against white, because buttons put --accent-text on top of it.
 *
 * The mood arrives from `GET /api/status`, polled. A mode id that changes when someone talks to
 * her is not a push-worthy event, and a poll needs no hub, no WebSocket through the tunnel, and no
 * change to the panel's `connect-src 'self'` CSP.
 */

/** Mode id → [from, to]. Ids not listed here are ignored, so the accent never blanks. */
const MOODS: Record<string, readonly [string, string]> = {
  SEETHING: ["#a3231f", "#8f1d3f"],
  ANNOYED: ["#a8532a", "#94441c"],
  BORED: ["#5d6d63", "#4f6470"],
  SMUG: ["#6b4bb0", "#8a3f95"],
  PLAYFUL: ["#0f7a8c", "#1a7f5a"],
  FOND: ["#a3365e", "#8c3a7d"],
  NEUTRAL: ["#2b6149", "#2f5d6b"],
};

/** One request a minute, well inside the `web:mood` hour-long TTL. */
const POLL_MS = 60_000;

/**
 * @param names Mode id → translated word, from the string table. Passed in rather than read here
 *   because `translator` needs the locale, which only a server component has.
 */
export function MoodAccent({ names, label }: { names: Record<string, string>; label: string }) {
  const [mood, setMood] = useState("NEUTRAL");

  useEffect(() => {
    const controller = new AbortController();

    async function read() {
      try {
        const response = await fetch("/api/status", { signal: controller.signal });

        if (!response.ok) {
          return;
        }

        const body: unknown = await response.json();
        const next =
          body && typeof body === "object" && "mood" in body
            ? (body as { mood: unknown }).mood
            : null;

        // Only ids we have colours for: the API is ours, but a persona edit could add a mode
        // before this file learns about it, and an unknown id must not blank the accent.
        if (typeof next === "string" && next in MOODS) {
          setMood(next);
        }
      } catch {
        // Offline, or the bot is down. The page keeps the colour it has — a panel that goes grey
        // the moment one poll fails is worse than one that is briefly out of date.
      }
    }

    void read();
    const timer = setInterval(() => void read(), POLL_MS);

    return () => {
      controller.abort();
      clearInterval(timer);
    };
  }, []);

  const [from, to] = MOODS[mood] ?? MOODS.NEUTRAL;
  const word = names[mood] ?? names.NEUTRAL ?? "";

  return (
    <>
      {/*
        A style element rather than inline styles: the drift needs a named animation, which an
        inline style cannot express, and 'unsafe-inline' is already in the panel's style-src.
        --accent is registered with @property in globals.css — an unregistered custom property is
        not interpolatable and would snap between the two shades instead of sliding.
      */}
      <style>{`
        :root {
          --accent-from: ${from};
          --accent-to: ${to};
          --accent-soft: ${from}14;
          animation: mood-drift 14s ease-in-out infinite alternate;
        }
      `}</style>

      {/* The accent has a shape as well as a colour, and the shape carries the word (WCAG 1.4.1):
          hovering names the mood, and the live region says it when it changes. */}
      <span className="dot" title={label.replace("{mood}", word)} aria-hidden="true" />
      <span className="sr-only" role="status" aria-live="polite">
        {label.replace("{mood}", word)}
      </span>
    </>
  );
}

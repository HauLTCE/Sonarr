import type { Locale } from "./strings";

/**
 * Dates and numbers, formatted with `Intl` — already in the runtime, already locale-aware, and the
 * only thing a date library would add here is a megabyte.
 *
 * Everything renders in the viewer's own locale but the server's timezone, because these pages are
 * server-rendered and the browser's zone never reaches us. UTC is the honest choice: a timestamp
 * quietly shown in the wrong zone is worse than one labelled as UTC.
 */
const dateTime = (locale: Locale) =>
  new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
    timeZone: "UTC",
  });

const dateOnly = (locale: Locale) =>
  new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeZone: "UTC" });

export function when(locale: Locale, value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  const at = new Date(value);

  return Number.isNaN(at.getTime()) ? "—" : `${dateTime(locale).format(at)} UTC`;
}

export function day(locale: Locale, value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  // A DateOnly arrives as "2026-07-28"; parsing it as UTC keeps it on the day it says.
  const at = new Date(`${value.slice(0, 10)}T00:00:00Z`);

  return Number.isNaN(at.getTime()) ? "—" : dateOnly(locale).format(at);
}

/** One bucket of the per-hour series: the day plus the hour, no minutes — the bucket is an hour. */
export function hour(locale: Locale, value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  const at = new Date(value);

  // Components, not `dateStyle` + `hour`: Intl treats the two families as mutually exclusive and
  // throws `TypeError: Invalid option : option` when they are mixed. That threw on every row of the
  // series, so the whole Stats page 500'd while the API behind it was returning 200 — and the error
  // surfaced as `Array.map` in a minified chunk, which reads like a data problem rather than a bad
  // formatter option. Any date+time formatter added here needs the same treatment.
  return Number.isNaN(at.getTime())
    ? "—"
    : `${hourFormat(locale).format(at)} UTC`;
}

const hourFormat = (locale: Locale) =>
  new Intl.DateTimeFormat(locale, {
    month: "short",
    day: "numeric",
    hour: "numeric",
    timeZone: "UTC",
  });

export function count(locale: Locale, value: number): string {
  return new Intl.NumberFormat(locale).format(value);
}

/** A 0–1 fraction as a percentage width, clamped so a bad number cannot break the layout. */
export function percent(fraction: number): string {
  return `${Math.round(Math.min(1, Math.max(0, fraction)) * 100)}%`;
}

import type { Metadata } from "next";

import { currentLocale } from "@/lib/locale";

import "./globals.css";

/**
 * Sonarr branding only (docs/09 naming rule) — no framework or vendor names in anything a visitor
 * sees. No web fonts either: `next/font/google` downloads at build time, and a container build on
 * a box behind a tunnel should not need the internet to produce a page of tables.
 */
export const metadata: Metadata = {
  title: "Sonarr",
  description: "Your data, your settings, in one place.",
  robots: { index: false, follow: false },
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  // lang has to be the real locale: a screen reader picks its voice from it.
  const locale = await currentLocale();

  return (
    <html lang={locale}>
      <body>{children}</body>
    </html>
  );
}

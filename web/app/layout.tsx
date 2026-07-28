import type { Metadata, Viewport } from "next";
import { Inter, JetBrains_Mono, Plus_Jakarta_Sans } from "next/font/google";

import { currentLocale } from "../lib/locale";

import "./globals.css";

/**
 * `next/font/google` downloads these at build time and serves them from `/_next/static/media`, so
 * they load under the panel's own CSP (`default-src 'self'`) with no Google connection at runtime.
 */
const body = Plus_Jakarta_Sans({
  subsets: ["latin", "latin-ext"],
  display: "swap",
  variable: "--font-body",
});

const heading = Inter({
  subsets: ["latin", "latin-ext"],
  display: "swap",
  variable: "--font-head",
});

const mono = JetBrains_Mono({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-mono",
});

export const metadata: Metadata = {
  // A template rather than a bare string: every page sets its own `title`, and 2.4.2 wants that
  // title to describe the page. Without this, fourteen tabs, bookmarks and history entries all
  // read "Sonarr" and two bookmarks are indistinguishable. `default` covers the pages that set
  // none (and 404).
  title: { template: "%s · Sonarr", default: "Sonarr" },
  description: "Your data, your server's settings, and the bot's health.",
  // The panel is behind a login and holds personal data; there is nothing here for a crawler.
  robots: { index: false, follow: false },
};

export const viewport: Viewport = {
  // Named so a dark page does not flash a light browser chrome on load.
  colorScheme: "dark",
  themeColor: "#0c0e10",
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const locale = await currentLocale();

  return (
    <html lang={locale} className={`${body.variable} ${heading.variable} ${mono.variable}`}>
      <body>{children}</body>
    </html>
  );
}

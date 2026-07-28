import type { Metadata } from "next";
import { Inter, JetBrains_Mono, Plus_Jakarta_Sans } from "next/font/google";

import { currentLocale } from "@/lib/locale";

import "./globals.css";

/**
 * Sonarr branding only (docs/09 naming rule) — no framework or vendor names in anything a visitor
 * sees.
 *
 * The three faces are fetched by `next/font/google` at build time and served from our own origin,
 * so a running container makes no request to a font CDN and the CSP stays `default-src 'self'`.
 * The build stage already needs the network for `npm ci`, so this costs no new reachability.
 */
const heading = Inter({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-heading",
});

const body = Plus_Jakarta_Sans({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-sans",
});

const mono = JetBrains_Mono({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-mono",
});

export const metadata: Metadata = {
  title: "Sonarr",
  description: "Your data, your settings, in one place.",
  robots: { index: false, follow: false },
};

export const viewport = {
  // Matches --bg, so the mobile browser chrome does not sit in a different colour to the page.
  themeColor: "#f3f5f2",
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  // lang has to be the real locale: a screen reader picks its voice from it.
  const locale = await currentLocale();

  return (
    <html lang={locale} className={`${heading.variable} ${body.variable} ${mono.variable}`}>
      <body>{children}</body>
    </html>
  );
}

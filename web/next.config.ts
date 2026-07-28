import type { NextConfig } from "next";

/**
 * The panel is served from the same origin as the API it reads (docs/09). `/api/*` is rewritten to
 * the bot's Kestrel port rather than called cross-origin from the browser, which is what makes the
 * session cookie work without CORS, without a preflight on every write, and without this app ever
 * having to handle a Set-Cookie itself.
 */
const api = process.env.SONARR_API_URL ?? "http://127.0.0.1:5088";

const nextConfig: NextConfig = {
  // Standalone traces the server's reachable dependencies into .next/standalone (a trimmed
  // node_modules plus server.js), so the runtime image needs no npm at all (docs/11).
  output: "standalone",

  poweredByHeader: false,

  async rewrites() {
    return [{ source: "/api/:path*", destination: `${api}/api/:path*` }];
  },

  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "same-origin" },
          // There is no third-party anything on these pages, so the strictest useful policy is
          // also the accurate one. 'unsafe-inline' covers Next's own hydration script tags.
          {
            key: "Content-Security-Policy",
            value: [
              "default-src 'self'",
              "script-src 'self' 'unsafe-inline'",
              "style-src 'self' 'unsafe-inline'",
              "img-src 'self' data:",
              "connect-src 'self'",
              "frame-ancestors 'none'",
              "base-uri 'self'",
              "form-action 'self'",
            ].join("; "),
          },
        ],
      },
    ];
  },
};

export default nextConfig;

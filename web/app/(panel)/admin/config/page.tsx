import { apiGet } from "@/lib/api";
import { guildScope, type SearchParams } from "@/lib/guilds";
import { translator } from "@/lib/strings";

import { Failure } from "../../../components/Failure";
import { GuildPicker } from "../../../components/GuildPicker";
import { NoGuilds } from "../../../components/NoGuilds";

import { ConfigForm, type ConfigRow } from "./ConfigForm";

/** The API returns the whole catalog keyed by config key, unset values included. */
type ConfigResponse = Record<string, { value: string | null; kind: string }>;

/** Every config key for one server, each editable in place (checklist 296). */
export default async function AdminConfigPage({ searchParams }: { searchParams: SearchParams }) {
  const scope = await guildScope(searchParams);

  if (!scope.ok) {
    return scope.failure === null ? (
      <NoGuilds locale={scope.locale} />
    ) : (
      <Failure locale={scope.locale} failure={scope.failure} />
    );
  }

  const { locale, guild, options } = scope;
  const t = translator(locale);
  const result = await apiGet<ConfigResponse>(`/api/admin/config/${guild.guildId}`);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  const rows: ConfigRow[] = Object.entries(result.data)
    .map(([key, entry]) => ({ key, value: entry.value, kind: entry.kind }))
    .sort((a, b) => a.key.localeCompare(b.key));

  return (
    <>
      <h1>{t("admin.config.title")}</h1>

      <GuildPicker
        locale={locale}
        path="/admin/config"
        options={options}
        selected={guild.guildId}
      />

      <section className="card">
        <div className="scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">{t("admin.config.key")}</th>
                <th scope="col">{t("admin.config.value")}</th>
                <th scope="col">{t("common.actions")}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <ConfigForm key={row.key} locale={locale} guildId={guild.guildId} row={row} />
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <p className="hint">{t("admin.config.note")}</p>
    </>
  );
}

import { apiGet } from "@/lib/api";
import { guildScope, type SearchParams } from "@/lib/guilds";
import { translator } from "@/lib/strings";

import { Failure } from "../../../components/Failure";
import { GuildPicker } from "../../../components/GuildPicker";
import { NoGuilds } from "../../../components/NoGuilds";

import { FlagToggle, type FlagRow } from "./FlagToggle";

/** Per-module feature flags for one server (checklist 297). */
export default async function AdminFlagsPage({ searchParams }: { searchParams: SearchParams }) {
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
  const result = await apiGet<FlagRow[]>(`/api/admin/flags/${guild.guildId}`);

  if (!result.ok) {
    return <Failure locale={locale} failure={result.failure} />;
  }

  return (
    <>
      <h1>{t("admin.flags.title")}</h1>

      <GuildPicker locale={locale} path="/admin/flags" options={options} selected={guild.guildId} />

      <section className="card">
        <div className="scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">{t("admin.flags.feature")}</th>
                <th scope="col">{t("admin.flags.state")}</th>
                <th scope="col">{t("admin.flags.source")}</th>
                <th scope="col">{t("common.actions")}</th>
              </tr>
            </thead>
            <tbody>
              {result.data.map((row) => (
                <FlagToggle
                  key={row.feature}
                  locale={locale}
                  guildId={guild.guildId}
                  row={row}
                />
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <p className="hint">{t("admin.flags.note")}</p>
    </>
  );
}

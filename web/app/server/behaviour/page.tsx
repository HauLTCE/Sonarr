import { apiGet } from "../../../lib/api";
import { configFields, type Directory } from "../../../lib/config";
import { pageTitle } from "../../../lib/locale";
import { panel, type Query } from "../../../lib/panel";
import type { StringKey } from "../../../lib/strings";

import { Fail } from "../../components/Fail";
import { Frame, PageHead } from "../../components/Frame";
import { ConfigForm } from "./ConfigForm";
import { FlagToggles, type Flag } from "./FlagToggles";

type Flags = { feature: string; enabled: boolean; source: string }[];
type Config = Record<
  string,
  { value: string | null; kind: string; minimum: number | null; maximum: number | null }
>;

export const generateMetadata = () => pageTitle("nav.behaviour");

/**
 * The only page in the guild tier that writes. Features first, because turning an area off is the
 * change with the biggest effect and the least to explain; the value fields come after.
 *
 * The wording for both is resolved here and handed to the client components, so neither of them
 * carries a dictionary.
 */
export default async function BehaviourPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("server/behaviour", searchParams);
  const id = guild!.guildId;

  const [flags, config, directory] = await Promise.all([
    apiGet<Flags>(`/api/admin/flags/${id}`),
    apiGet<Config>(`/api/admin/config/${id}`),
    apiGet<NonNullable<Directory>>(`/api/admin/directory/${id}`),
  ]);

  // A failed directory is not a failed page: every channel and role field falls back to the text box
  // it used to be, which still accepts a pasted id. Losing the names is worth less than losing the
  // ability to change a setting.
  const names: Directory = directory.ok ? directory.data : null;

  // A feature the API reports that this panel has no wording for still renders, under its own id —
  // adding one to the domain must never make it invisible here.
  const named: Flag[] = flags.ok
    ? flags.data.map((f) => {
        const labelKey = `feat.${f.feature}` as StringKey;
        const label = t(labelKey);
        const known = label !== labelKey;

        return {
          feature: f.feature,
          label: known ? label : f.feature,
          hint: known ? t(`feat.${f.feature}.hint` as StringKey) : t("behaviour.unknown"),
          enabled: f.enabled,
          source: f.source,
        };
      })
    : [];

  return (
    <Frame session={session} current="server/behaviour" guild={guild} t={t}>
      <PageHead
        label={t("tier.guild")}
        title={t("behaviour.title")}
        lead={t("behaviour.lead", { guild: guild!.name })}
      />

      {flags.ok ? (
        <FlagToggles
          guildId={id}
          flags={named}
          labels={{
            title: t("behaviour.features"),
            lead: t("behaviour.featuresLead"),
            on: t("behaviour.on"),
            off: t("behaviour.off"),
            working: t("behaviour.saving"),
            failed: t("behaviour.failed"),
            sourceDefault: t("behaviour.sourceDefault"),
            sourceGuild: t("behaviour.sourceGuild"),
            sourceGlobal: t("behaviour.sourceGlobal"),
          }}
        />
      ) : (
        <Fail failure={flags.failure} t={t} />
      )}

      {config.ok ? (
        <ConfigForm
          guildId={id}
          fields={configFields(config.data, t, names)}
          labels={{
            title: t("behaviour.values"),
            lead: names === null ? t("behaviour.valuesLead") : t("behaviour.valuesLeadNamed"),
            save: t("behaviour.save"),
            saving: t("behaviour.saving"),
            saved: t("behaviour.saved"),
            reset: t("behaviour.reset"),
            on: t("behaviour.on"),
            off: t("behaviour.off"),
            failed: t("behaviour.failed"),
            none: t("behaviour.none"),
            gone: t("behaviour.gone"),
            weightAdd: t("behaviour.weightAdd"),
            weightAddNone: t("behaviour.weightAddNone"),
            weightRemove: t("behaviour.weightRemove"),
            weightPercent: t("behaviour.weightPercent"),
            weightEmpty: t("behaviour.weightEmpty"),
          }}
        />
      ) : (
        <Fail failure={config.failure} t={t} />
      )}
    </Frame>
  );
}

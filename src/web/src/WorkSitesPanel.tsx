import { useState } from "react";
import { Globe, Plus, Trash2 } from "lucide-react";
import { api, type Settings } from "./api";

export function WorkSitesPanel({
  settings,
  changed,
}: {
  settings: Settings;
  changed: () => Promise<void>;
}) {
  const [site, setSite] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState(false);
  const sites = settings.workSites ?? [];
  async function update(workSites: string[], adding = false) {
    setBusy(true);
    setError("");
    setSaved(false);
    try {
      await api("/settings", "PUT", { ...settings, workSites });
      await changed();
      if (adding) setSite("");
      setSaved(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Не удалось сохранить сайты.");
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="section" aria-labelledby="sites-title">
      <div className="section-heading">
        <div>
          <h2 id="sites-title">Рабочие сайты</h2>
          <p>Firefox · Домен и поддомены · Новые посещения</p>
        </div>
        <Globe size={19} />
      </div>
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      <div className="rules">
        {sites.map((domain) => (
          <div className="rule" key={domain}>
            <span className="app-symbol">
              <Globe size={17} />
            </span>
            <span>{domain}</span>
            <button
              className="icon-button"
              disabled={busy}
              title={`Удалить ${domain} из рабочих сайтов`}
              aria-label={`Удалить ${domain} из рабочих сайтов`}
              onClick={() =>
                void update(sites.filter((value) => value !== domain))
              }
            >
              <Trash2 size={15} />
            </button>
          </div>
        ))}
      </div>
      {sites.length === 0 && <p className="muted">Рабочих сайтов пока нет</p>}
      <form
        className="add-process"
        onSubmit={(event) => {
          event.preventDefault();
          void update([...sites, site], true);
        }}
      >
        <input
          aria-label="Домен рабочего сайта"
          placeholder="docs.unity3d.com"
          value={site}
          onChange={(event) => setSite(event.target.value)}
          maxLength={2048}
          required
          autoCapitalize="none"
          spellCheck={false}
        />
        <button type="submit" disabled={busy || !site.trim()}>
          <Plus size={16} />
          Добавить сайт
        </button>
      </form>
      <span role="status">{saved ? "Список сайтов сохранён" : ""}</span>
    </section>
  );
}

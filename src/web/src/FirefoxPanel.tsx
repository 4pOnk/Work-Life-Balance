import { Download, Globe, Link2 } from "lucide-react";
import type { Status } from "./api";

export function FirefoxPanel({ status }: { status: Status["firefox"] }) {
  return (
    <section className="section" aria-labelledby="firefox-title">
      <div className="section-heading">
        <h2 id="firefox-title">Firefox</h2>
        <Globe size={19} />
      </div>
      <div className="firefox-status">
        <Link2 size={18} />
        <div>
          <strong>
            {status.connected
              ? "Расширение подключено"
              : "Нет подключения к расширению"}
          </strong>
          <p>
            {status.lastSeen
              ? `Последняя связь: ${new Date(status.lastSeen).toLocaleTimeString("ru-RU")}`
              : "Локальный помощник ожидает Firefox"}
          </p>
        </div>
        <a className="download-link" href="/api/v1/firefox/package" download>
          <Download size={16} />
          Пакет без подписи
        </a>
      </div>
    </section>
  );
}

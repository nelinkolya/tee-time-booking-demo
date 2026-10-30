import { useEffect, useState } from "react";
import { getVersion, type AppVersion } from "../api";

const repoUrl = "https://github.com/nelinkolya/tee-time-booking-demo";

export function VersionFooter() {
  const [version, setVersion] = useState<AppVersion>();
  useEffect(() => {
    getVersion().then(setVersion, () => undefined);
  }, []);
  if (!version) return null;

  const commit = version.commit?.slice(0, 7);
  const built = version.builtAtUtc && new Date(version.builtAtUtc).toISOString().slice(0, 16).replace("T", " ");
  return (
    <footer className="version">
      v{version.version}
      {commit && (
        <>
          {" · "}
          <a href={`${repoUrl}/commit/${version.commit}`} target="_blank" rel="noreferrer" title={version.commit ?? undefined}>
            {commit}
          </a>
        </>
      )}
      {built && ` · built ${built} UTC`}
      {` · ${version.environment}`}
    </footer>
  );
}

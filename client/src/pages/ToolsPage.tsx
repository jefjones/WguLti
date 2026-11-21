import { FormEvent, useEffect, useState } from "react";
import { ApiError, Registration, api, scopes } from "../api";

const empty = {
  name: "Demo Tool",
  toolDomain: "localhost",
  oidcInitiationUrl: "https://localhost:7091/oidc/login",
  targetLinkUri: "https://localhost:7091/launch",
  redirectUris: "https://localhost:7091/launch",
  jwksUri: "https://localhost:7091/.well-known/jwks.json",
  publicJwk: "",
  privacyLevel: "name_only",
  scopes: ["https://purl.imsglobal.org/spec/lti-ags/scope/score"],
};

export function ToolsPage() {
  const [tools, setTools] = useState<Registration[]>([]);
  const [form, setForm] = useState(empty);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<Registration | null>(null);

  async function load() {
    setTools(await api<Registration[]>("/api/admin/registrations"));
  }

  useEffect(() => {
    load().catch((err: unknown) => setError(err instanceof ApiError ? err.message : "API is not reachable."));
  }, []);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setCreated(null);
    try {
      const registration = await api<Registration>("/api/admin/registrations", {
        method: "POST",
        body: JSON.stringify({
          name: form.name,
          toolDomain: form.toolDomain,
          oidcInitiationUrl: form.oidcInitiationUrl,
          targetLinkUri: form.targetLinkUri,
          redirectUris: form.redirectUris.split(/\s+/).filter(Boolean),
          jwksUri: form.jwksUri || null,
          publicJwk: form.publicJwk || null,
          privacyLevel: form.privacyLevel,
          scopes: form.scopes,
          enabled: true,
        }),
      });
      setCreated(registration);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not create the registration.");
    }
  }

  function toggleScope(scope: string) {
    setForm((current) => ({
      ...current,
      scopes: current.scopes.includes(scope)
        ? current.scopes.filter((item) => item !== scope)
        : [...current.scopes, scope],
    }));
  }

  return (
    <>
      <h1>Tool registrations</h1>
      <p className="lede">A registration is the developer key. The platform generates the client id.</p>
      {error && <div className="banner">{error}</div>}
      {created && (
        <div className="ok">
          Created <strong>{created.name}</strong>. Give the tool this client id:{" "}
          <span className="mono">{created.clientId}</span>
        </div>
      )}
      <div className="card">
        <table>
          <thead>
            <tr><th>Name</th><th>Client id</th><th>Domain</th><th>State</th><th>Privacy</th></tr>
          </thead>
          <tbody>
            {tools.length === 0 && <tr><td colSpan={5}>No tools yet.</td></tr>}
            {tools.map((tool) => (
              <tr key={tool.id}>
                <td>{tool.name}</td>
                <td className="mono">{tool.clientId}</td>
                <td>{tool.toolDomain}</td>
                <td>{tool.state}</td>
                <td>{tool.privacyLevel}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <form className="card grid" onSubmit={onSubmit}>
        <label>Name<input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required /></label>
        <label>Tool domain<input value={form.toolDomain} onChange={(e) => setForm({ ...form, toolDomain: e.target.value })} required /></label>
        <label className="span-2">OIDC login URL<input value={form.oidcInitiationUrl} onChange={(e) => setForm({ ...form, oidcInitiationUrl: e.target.value })} required /></label>
        <label className="span-2">Target link URI<input value={form.targetLinkUri} onChange={(e) => setForm({ ...form, targetLinkUri: e.target.value })} required /></label>
        <label className="span-2">Redirect URIs<textarea value={form.redirectUris} onChange={(e) => setForm({ ...form, redirectUris: e.target.value })} /></label>
        <label className="span-2">JWKS URL<input value={form.jwksUri} onChange={(e) => setForm({ ...form, jwksUri: e.target.value })} /></label>
        <label className="span-2">Public JWK<textarea value={form.publicJwk} onChange={(e) => setForm({ ...form, publicJwk: e.target.value })} placeholder='{"kty":"RSA",...}' /></label>
        <label>Privacy
          <select value={form.privacyLevel} onChange={(e) => setForm({ ...form, privacyLevel: e.target.value })}>
            <option value="anonymous">anonymous</option>
            <option value="name_only">name_only</option>
            <option value="email_only">email_only</option>
            <option value="public">public</option>
          </select>
        </label>
        <div>
          {scopes.map((scope) => (
            <label key={scope} className="row">
              <input type="checkbox" checked={form.scopes.includes(scope)} onChange={() => toggleScope(scope)} />
              <span className="mono">{scope.split("/").pop()}</span>
            </label>
          ))}
        </div>
        <div className="span-2"><button type="submit">Create registration</button></div>
      </form>
    </>
  );
}

import { FormEvent, useEffect, useState } from "react";
import { ApiError, Deployment, Overview, api } from "../api";

export function DeploymentsPage() {
  const [overview, setOverview] = useState<Overview | null>(null);
  const [registrationId, setRegistrationId] = useState("");
  const [contextId, setContextId] = useState("");
  const [label, setLabel] = useState("Course navigation");
  const [error, setError] = useState<string | null>(null);

  async function load() {
    const data = await api<Overview>("/api/admin/overview");
    setOverview(data);
    setRegistrationId((current) => current || data.registrations[0]?.id || "");
    setContextId((current) => current || data.contexts[0]?.id || "");
  }

  useEffect(() => {
    load().catch((err: unknown) => setError(err instanceof ApiError ? err.message : "API is not reachable."));
  }, []);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api<Deployment>("/api/admin/deployments", {
        method: "POST",
        body: JSON.stringify({ registrationId, contextId, label, enabled: true }),
      });
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not create the deployment.");
    }
  }

  return (
    <>
      <h1>Deployments</h1>
      <p className="lede">Install a registration into one course. The deployment id is what the tool sees in the launch.</p>
      {error && <div className="banner">{error}</div>}
      <div className="card">
        <table>
          <thead>
            <tr><th>Tool</th><th>Deployment id</th><th>Course</th><th>Enabled</th></tr>
          </thead>
          <tbody>
            {(overview?.deployments.length ?? 0) === 0 && <tr><td colSpan={4}>No deployments yet.</td></tr>}
            {overview?.deployments.map((deployment) => (
              <tr key={deployment.id}>
                <td>{deployment.toolName}</td>
                <td className="mono">{deployment.deploymentId}</td>
                <td>{deployment.courseCode ?? "Account"}</td>
                <td>{deployment.isEnabled ? "yes" : "no"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <form className="card grid" onSubmit={onSubmit}>
        <label>Registration
          <select value={registrationId} onChange={(e) => setRegistrationId(e.target.value)} required>
            <option value="">Select a tool</option>
            {overview?.registrations.map((tool) => <option key={tool.id} value={tool.id}>{tool.name}</option>)}
          </select>
        </label>
        <label>Course
          <select value={contextId} onChange={(e) => setContextId(e.target.value)} required>
            <option value="">Select a course</option>
            {overview?.contexts.map((course) => (
              <option key={course.id} value={course.id}>{course.courseCode} — {course.title}</option>
            ))}
          </select>
        </label>
        <label className="span-2">Label<input value={label} onChange={(e) => setLabel(e.target.value)} /></label>
        <div className="span-2"><button type="submit">Install in course</button></div>
      </form>
    </>
  );
}

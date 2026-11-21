import { useEffect, useState } from "react";
import { ApiError, Deployment, LaunchInitiation, Overview, api } from "../api";

export function CoursePage() {
  const [overview, setOverview] = useState<Overview | null>(null);
  const [contextId, setContextId] = useState("");
  const [userId, setUserId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState("The launch frame is empty until you start a tool.");

  useEffect(() => {
    api<Overview>("/api/admin/overview")
      .then((data) => {
        setOverview(data);
        setContextId(data.contexts[0]?.id ?? "");
        setUserId(data.users[0]?.id ?? "");
      })
      .catch((err: unknown) => setError(err instanceof ApiError ? err.message : "API is not reachable. Start WguLti.Api and the database."));
  }, []);

  const course = overview?.contexts.find((item) => item.id === contextId);
  const deployments = overview?.deployments.filter((item) => item.contextId === contextId && item.isEnabled) ?? [];

  async function launch(deployment: Deployment) {
    setError(null);
    if (!userId) {
      setError("Pick a user. The demo seeder creates Ada Lovelace.");
      return;
    }
    try {
      const initiation = await api<LaunchInitiation>("/api/lti/launches", {
        method: "POST",
        body: JSON.stringify({
          deploymentId: deployment.id,
          userId,
          contextId,
          placement: "course_navigation",
        }),
      });
      const frame = document.getElementById("lti-frame") as HTMLIFrameElement | null;
      if (!frame) return;
      const form = document.createElement("form");
      form.method = "post";
      form.action = initiation.loginInitiationUrl;
      form.target = "lti-frame";
      Object.entries(initiation.fields).forEach(([name, value]) => {
        const input = document.createElement("input");
        input.type = "hidden";
        input.name = name;
        input.value = value;
        form.appendChild(input);
      });
      document.body.appendChild(form);
      form.submit();
      form.remove();
      setStatus(`Posted login initiation to ${initiation.loginInitiationUrl}.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Launch failed.");
    }
  }

  return (
    <>
      <h1>{course ? `${course.courseCode} ${course.title}` : "Course"}</h1>
      <p className="lede">Launch placeholder. The button posts the OIDC login form into the frame below.</p>
      {error && <div className="banner">{error}</div>}
      <div className="card row">
        <label>Course
          <select value={contextId} onChange={(e) => setContextId(e.target.value)}>
            {overview?.contexts.map((item) => (
              <option key={item.id} value={item.id}>{item.courseCode} — {item.title}</option>
            ))}
          </select>
        </label>
        <label>User
          <select value={userId} onChange={(e) => setUserId(e.target.value)}>
            {overview?.users.map((user) => (
              <option key={user.id} value={user.id}>{user.displayName || user.email}</option>
            ))}
          </select>
        </label>
      </div>
      <div className="card">
        {deployments.length === 0 && <p>No enabled tool is installed in this course.</p>}
        {deployments.map((deployment) => (
          <div className="row" key={deployment.id}>
            <div>
              <strong>{deployment.toolName}</strong>
              <div className="mono">{deployment.label || deployment.deploymentId}</div>
            </div>
            <button type="button" onClick={() => launch(deployment)}>Launch</button>
          </div>
        ))}
      </div>
      <p>{status}</p>
      <iframe id="lti-frame" name="lti-frame" title="LTI launch" className="frame" />
    </>
  );
}

import { NavLink, Route, Routes } from "react-router-dom";
import { CoursePage } from "./pages/CoursePage";
import { DeploymentsPage } from "./pages/DeploymentsPage";
import { ToolsPage } from "./pages/ToolsPage";

export function App() {
  return (
    <div className="shell">
      <nav className="nav">
        <h1 className="wordmark">WguLti</h1>
        <p className="tag">LTI 1.3 platform</p>
        <NavLink to="/" end>Tools</NavLink>
        <NavLink to="/deployments">Deployments</NavLink>
        <NavLink to="/course">Course</NavLink>
      </nav>
      <main>
        <Routes>
          <Route path="/" element={<ToolsPage />} />
          <Route path="/deployments" element={<DeploymentsPage />} />
          <Route path="/course" element={<CoursePage />} />
        </Routes>
      </main>
    </div>
  );
}

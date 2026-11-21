const base = import.meta.env.VITE_API_BASE ?? "";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(base + path, {
    ...init,
    headers: {
      Accept: "application/json",
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...init?.headers,
    },
  });
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  const payload = text ? JSON.parse(text) as { error_description?: string; title?: string; detail?: string } : {};
  if (!response.ok) {
    throw new ApiError(response.status, payload.error_description || payload.detail || payload.title || response.statusText);
  }
  return payload as T;
}

export interface Registration {
  id: string;
  clientId: string;
  name: string;
  toolDomain: string;
  oidcInitiationUrl: string;
  targetLinkUri: string;
  redirectUris: string[];
  jwksUri?: string | null;
  publicJwk?: string | null;
  scopes: string[];
  privacyLevel: string;
  state: string;
}

export interface Deployment {
  id: string;
  registrationId: string;
  clientId: string;
  toolName: string;
  deploymentId: string;
  contextId?: string | null;
  accountId?: string | null;
  courseCode?: string | null;
  label?: string | null;
  isEnabled: boolean;
}

export interface Overview {
  issuer: string;
  accounts: { id: string; name: string }[];
  users: { id: string; displayName?: string | null; email?: string | null }[];
  contexts: { id: string; courseCode: string; title: string; workflowState: string }[];
  registrations: Registration[];
  deployments: Deployment[];
}

export interface LaunchInitiation {
  loginInitiationUrl: string;
  fields: Record<string, string>;
}

export const scopes = [
  "https://purl.imsglobal.org/spec/lti-ags/scope/lineitem",
  "https://purl.imsglobal.org/spec/lti-ags/scope/lineitem.readonly",
  "https://purl.imsglobal.org/spec/lti-ags/scope/result.readonly",
  "https://purl.imsglobal.org/spec/lti-ags/scope/score",
  "https://purl.imsglobal.org/spec/lti-nrps/scope/contextmembership.readonly",
];

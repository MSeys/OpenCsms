/**
 * The dashboard's only door to the application. Every screen imports typed calls from here so the
 * network surface stays in one place.
 *
 * Auth is the browser session: `POST /api/auth/sign-in` sets an HttpOnly cookie that every request
 * carries; JavaScript never sees the credential. A 401 on a read means the session is gone, so the
 * shell sends the visitor back to the sign-in screen.
 */

export interface UserSession {
  userId: string;
  email: string;
  displayName: string;
  tenantId: string;
  role: string;
}

export interface Station {
  id: string;
  name: string;
  chargePointId: string;
  connectorCount: number;
  tariffId: string;
  lastSeenAtUtc: string | null;
}

export interface Connector {
  connectorId: number;
  status: string;
  errorCode: string;
  updatedAtUtc: string;
}

export interface PublicStation {
  id: string;
  name: string;
  chargePointId: string;
  lastSeenAtUtc: string | null;
  connectors: Connector[];
}

export interface ChargingSession {
  id: string;
  transactionId: number;
  tenantId: string;
  stationId: string;
  connectorId: number;
  startedAtUtc: string;
  endedAtUtc: string | null;
  energyKwh: number;
  isOpen: boolean;
}

export interface Invoice {
  id: string;
  sessionId: string;
  tenantId: string;
  energyKwh: number;
  energyAmount: number;
  startFeeAmount: number;
  idleHours: number;
  idleFeeAmount: number;
  total: number;
  currency: string;
  issuedAtUtc: string;
}

export interface Tariff {
  id: string;
  tenantId: string;
  name: string;
  energyPricePerKwh: number;
  startFee: number;
  idleFeePerHour: number;
  idleGracePeriod: string;
  currency: string;
}

export interface RemoteCommandResult {
  status: string;
}

export interface TariffUpdate {
  energyPricePerKwh: number;
  startFee: number;
  idleFeePerHour: number;
  /** The idle grace period the API binds, "HH:mm:ss". */
  idleGracePeriod: string;
}

/** The application's `application/problem+json` envelope, as an Error. */
export class ApiProblem extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly details: Record<string, string> | null = null
  ) {
    super(message);
    this.name = "ApiProblem";
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  get isForbidden(): boolean {
    return this.status === 403;
  }
}

let onUnauthorized: (() => void) | null = null;

/** Registered once by main.ts so any screen that loses its session is sent back to sign-in. */
export function setUnauthorizedHandler(handler: () => void): void {
  onUnauthorized = handler;
}

interface RequestOptions {
  body?: unknown;
  suppressUnauthorizedRedirect?: boolean;
}

async function request<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  let response: Response;
  try {
    response = await fetch(path, {
      method,
      headers,
      credentials: "same-origin",
      body: options.body === undefined ? undefined : JSON.stringify(options.body)
    });
  } catch {
    throw new ApiProblem(0, "network_error", "The OpenCSMS API is unreachable.");
  }

  if (response.status === 401 && !options.suppressUnauthorizedRedirect) {
    onUnauthorized?.();
  }

  if (!response.ok) {
    throw await problemFrom(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function problemFrom(response: Response): Promise<ApiProblem> {
  try {
    const payload = (await response.json()) as {
      code?: string;
      message?: string;
      detail?: string;
      title?: string;
    };
    return new ApiProblem(
      response.status,
      payload.code ?? "http_error",
      payload.message ?? payload.detail ?? payload.title ?? `Request failed with ${response.status}.`
    );
  } catch {
    return new ApiProblem(response.status, "http_error", `Request failed with ${response.status}.`);
  }
}

// ---------------------------------------------------------------- identity

/** Loads the signed-in user; a 401 is the anonymous answer the shell expects, not a failure. */
export function getSession(): Promise<UserSession> {
  return request<UserSession>("GET", "/api/auth/session", { suppressUnauthorizedRedirect: true });
}

export function signIn(email: string, password: string): Promise<UserSession> {
  return request<UserSession>("POST", "/api/auth/sign-in", {
    body: { email, password },
    suppressUnauthorizedRedirect: true
  });
}

export async function signOut(): Promise<void> {
  try {
    await request<void>("POST", "/api/auth/sign-out", { suppressUnauthorizedRedirect: true });
  } catch {
    // Signing out is best-effort: the local state is cleared either way.
  }
}

// ---------------------------------------------------------------- dashboard reads

export function listStations(): Promise<Station[]> {
  return request<Station[]>("GET", "/api/dashboard/stations");
}

export function getStation(stationId: string): Promise<Station> {
  return request<Station>("GET", `/api/dashboard/stations/${encodeURIComponent(stationId)}`);
}

export function listStationSessions(stationId: string): Promise<ChargingSession[]> {
  return request<ChargingSession[]>(
    "GET",
    `/api/dashboard/stations/${encodeURIComponent(stationId)}/sessions`
  );
}

export function listInvoices(): Promise<Invoice[]> {
  return request<Invoice[]>("GET", "/api/dashboard/invoices");
}

export function getInvoice(invoiceId: string): Promise<Invoice> {
  return request<Invoice>("GET", `/api/dashboard/invoices/${encodeURIComponent(invoiceId)}`);
}

export function listTariffs(): Promise<Tariff[]> {
  return request<Tariff[]>("GET", "/api/dashboard/tariffs");
}

// ---------------------------------------------------------------- dashboard mutations

/**
 * The dashboard's cookie-authenticated mutations. Each one fetches a fresh anti-forgery request
 * token first and sends it back in the X-XSRF-TOKEN header the API validates; a viewer calling
 * one gets the API's 403, exactly as the contract tests pin it.
 */
async function mutation<T>(method: string, path: string, body: unknown): Promise<T> {
  const tokens = await request<{ requestToken: string }>("GET", "/api/auth/xsrf", {
    suppressUnauthorizedRedirect: true
  });

  let response: Response;
  try {
    response = await fetch(path, {
      method,
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
        "X-XSRF-TOKEN": tokens.requestToken
      },
      credentials: "same-origin",
      body: JSON.stringify(body)
    });
  } catch {
    throw new ApiProblem(0, "network_error", "The OpenCSMS API is unreachable.");
  }

  if (response.status === 401) {
    onUnauthorized?.();
  }

  if (!response.ok) {
    throw await problemFrom(response);
  }

  return (await response.json()) as T;
}

export function updateTariff(tariffId: string, update: TariffUpdate): Promise<Tariff> {
  return mutation<Tariff>("PUT", `/api/dashboard/tariffs/${encodeURIComponent(tariffId)}`, update);
}

export function remoteStart(
  stationId: string,
  idTag: string,
  connectorId: number | null
): Promise<RemoteCommandResult> {
  return mutation<RemoteCommandResult>(
    "POST",
    `/api/dashboard/stations/${encodeURIComponent(stationId)}/remote-start`,
    { idTag, connectorId }
  );
}

export function remoteStop(sessionId: string): Promise<RemoteCommandResult> {
  return mutation<RemoteCommandResult>(
    "POST",
    `/api/dashboard/sessions/${encodeURIComponent(sessionId)}/remote-stop`,
    {}
  );
}

/** Downloads the tenant's monthly invoice export the way the suite's Sheets journey reads it. */
export async function downloadInvoiceExport(month: string): Promise<void> {
  let response: Response;
  try {
    response = await fetch(`/api/invoices/export?month=${encodeURIComponent(month)}`, {
      credentials: "same-origin",
      headers: {
        Accept: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
      }
    });
  } catch {
    throw new ApiProblem(0, "network_error", "The OpenCSMS API is unreachable.");
  }

  if (response.status === 401) {
    onUnauthorized?.();
  }

  if (!response.ok) {
    throw await problemFrom(response);
  }

  const disposition = response.headers.get("content-disposition") ?? "";
  const fileName = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? `invoices-${month}.xlsx`;
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

// ---------------------------------------------------------------- public status

export function listPublicStations(): Promise<PublicStation[]> {
  return request<PublicStation[]>("GET", "/api/status/stations");
}

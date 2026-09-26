import { reactive, readonly } from "vue";
import * as api from "./api";

export type SessionStatus = "unknown" | "anonymous" | "ready" | "error";

interface SessionState {
  status: SessionStatus;
  user: api.UserSession | null;
  error: string | null;
}

const state = reactive<SessionState>({
  status: "unknown",
  user: null,
  error: null
});

let started: Promise<void> | null = null;

async function load(): Promise<void> {
  try {
    state.user = await api.getSession();
    state.status = "ready";
    state.error = null;
  } catch (error) {
    state.user = null;
    if (error instanceof api.ApiProblem && error.isUnauthorized) {
      state.status = "anonymous";
      state.error = null;
    } else {
      state.status = "error";
      state.error = error instanceof Error ? error.message : "The OpenCSMS API is unreachable.";
    }
  }
}

export const session = {
  state: readonly(state),

  /** True only for the operator admin role; operator actions are hidden from viewers. */
  get isOperator(): boolean {
    return state.user?.role === "operator";
  },

  /** Runs the first load exactly once; the router guard awaits this before every navigation. */
  ensureInitialized(): Promise<void> {
    started ??= load();
    return started;
  },

  async signIn(email: string, password: string): Promise<void> {
    state.user = await api.signIn(email, password);
    state.status = "ready";
    state.error = null;
  },

  async signOut(): Promise<void> {
    await api.signOut();
    state.user = null;
    state.status = "anonymous";
    state.error = null;
  },

  markAnonymous(): void {
    if (state.status === "ready") {
      state.user = null;
      state.status = "anonymous";
    }
  }
};

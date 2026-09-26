<script setup lang="ts">
import { useRouter } from "vue-router";
import { session } from "./session";

const router = useRouter();

async function signOut(): Promise<void> {
  await session.signOut();
  await router.push({ name: "sign-in" });
}
</script>

<template>
  <div class="shell">
    <header class="topbar">
      <RouterLink class="brand" to="/" data-testid="brand">
        <svg class="brand__mark" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M13.2 1.8 4.4 13.1c-.4.5 0 1.2.6 1.2h4.6l-1 7.3c-.1.7.8 1.1 1.3.6l8.9-11.3c.4-.5 0-1.2-.6-1.2h-4.7l1-7.3c.1-.7-.8-1.1-1.3-.6Z" />
        </svg>
        <span class="brand__name">OpenCSMS</span>
        <span class="brand__product">Operator</span>
      </RouterLink>

      <div v-if="session.state.user" class="topbar__session">
        <span class="topbar__user" data-testid="session-user">{{ session.state.user.displayName }}</span>
        <span class="topbar__tenant" data-testid="session-tenant">{{ session.state.user.tenantId }}</span>
        <span class="badge" data-tone="accent" data-testid="session-role">{{ session.state.user.role }}</span>
        <button class="button button--quiet topbar__signout" type="button" data-testid="sign-out" @click="signOut">
          Sign out
        </button>
      </div>
      <RouterLink v-else class="topbar__signin" to="/sign-in" data-testid="sign-in-link">Sign in</RouterLink>
    </header>

    <div class="shell__body">
      <nav v-if="session.state.user" class="sidenav" aria-label="Primary">
        <RouterLink class="sidenav__link" to="/" data-testid="nav-stations">Stations</RouterLink>
        <RouterLink class="sidenav__link" to="/invoices" data-testid="nav-invoices">Invoices</RouterLink>
        <RouterLink class="sidenav__link" to="/tariffs" data-testid="nav-tariffs">Tariffs</RouterLink>
        <RouterLink class="sidenav__link" to="/status" data-testid="nav-status">Public status</RouterLink>
      </nav>

      <main class="content">
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
.shell {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.topbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px 20px;
  min-height: 52px;
  padding: 8px 22px;
  background: var(--oc-ink-deep);
  color: #e7efed;
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: inherit;
}

.brand:hover {
  text-decoration: none;
}

.brand__mark {
  width: 20px;
  height: 20px;
  fill: #46c592;
}

.brand__name {
  font-family: var(--font-display);
  font-size: 17px;
  font-weight: 600;
  letter-spacing: -0.01em;
}

.brand__product {
  border: 1px solid rgb(231 239 237 / 25%);
  border-radius: 999px;
  padding: 1px 8px;
  color: #a9bdb8;
  font-size: 11.5px;
}

.topbar__session {
  display: flex;
  align-items: center;
  gap: 12px;
}

.topbar__user {
  color: #e7efed;
  font-size: 13.5px;
  font-weight: 600;
}

.topbar__tenant {
  color: #a9bdb8;
  font-family: var(--font-mono);
  font-size: 12px;
}

.topbar__signout {
  border-color: rgb(231 239 237 / 25%);
  background: transparent;
  color: #e7efed;
}

.topbar__signout:hover:not(:disabled) {
  border-color: rgb(231 239 237 / 55%);
  background: rgb(231 239 237 / 8%);
}

.topbar__signin {
  color: #e7efed;
  font-size: 13.5px;
  font-weight: 600;
}

.shell__body {
  flex: 1;
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  align-content: flex-start;
}

.sidenav {
  flex: 0 0 196px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 22px 12px;
}

.sidenav__link {
  border-left: 3px solid transparent;
  border-radius: 0 7px 7px 0;
  padding: 8px 12px;
  color: var(--oc-muted);
  font-size: 14px;
  font-weight: 600;
}

.sidenav__link:hover {
  background: #e6ecea;
  color: var(--oc-ink);
  text-decoration: none;
}

.sidenav__link[aria-current="page"] {
  border-left-color: var(--oc-accent);
  background: var(--oc-surface);
  color: var(--oc-ink);
}

.content {
  flex: 1 1 560px;
  min-width: 0;
  padding: 26px 28px 72px;
}

/* Below the point where the content column wraps, the nav becomes a horizontal strip: the same links,
   a shape that costs one row instead of a column. */
@media (max-width: 760px) {
  .sidenav {
    flex: 1 1 100%;
    flex-direction: row;
    overflow-x: auto;
    padding: 10px 16px 0;
  }

  .sidenav__link {
    border-left: 0;
    border-bottom: 3px solid transparent;
    border-radius: 7px 7px 0 0;
    white-space: nowrap;
  }

  .sidenav__link[aria-current="page"] {
    border-bottom-color: var(--oc-accent);
  }

  .content {
    padding: 20px 18px 64px;
  }
}
</style>

<script setup lang="ts">
import { ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ApiProblem } from "../api";
import { session } from "../session";

const route = useRoute();
const router = useRouter();
const email = ref("");
const password = ref("");
const error = ref<string | null>(null);
const busy = ref(false);

async function submit(): Promise<void> {
  const address = email.value.trim();
  if (!address || !password.value) {
    error.value = "Enter the account's email and password.";
    return;
  }

  busy.value = true;
  error.value = null;
  try {
    await session.signIn(address, password.value);
    const redirect = typeof route.query.redirect === "string" ? route.query.redirect : "/";
    await router.push(redirect);
  } catch (cause) {
    error.value =
      cause instanceof ApiProblem && cause.isUnauthorized
        ? "That email and password do not match an account."
        : "Sign-in failed. Check that the OpenCSMS API is running.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <section class="login" data-testid="sign-in-page">
    <div class="login__card panel">
      <div class="login__intro">
        <h1>Sign in to OpenCSMS</h1>
        <p class="muted">
          The operator dashboard shows the charge points, sessions, invoices and tariffs of one charging
          network. Sign in with the account your operator admin provisioned.
        </p>
      </div>

      <form class="login__form" data-testid="sign-in-form" novalidate @submit.prevent="submit">
        <div class="field">
          <label for="sign-in-email">Email</label>
          <input
            id="sign-in-email"
            v-model="email"
            class="input"
            type="email"
            name="email"
            autocomplete="username"
            data-testid="sign-in-email"
            :aria-invalid="error ? 'true' : undefined"
          >
        </div>

        <div class="field">
          <label for="sign-in-password">Password</label>
          <input
            id="sign-in-password"
            v-model="password"
            class="input"
            type="password"
            name="password"
            autocomplete="current-password"
            data-testid="sign-in-password"
            :aria-invalid="error ? 'true' : undefined"
          >
        </div>

        <button class="button button--primary" type="submit" data-testid="sign-in-submit" :disabled="busy">
          Sign in
        </button>

        <p class="login__error" role="alert" data-testid="sign-in-error">{{ error ?? "" }}</p>
      </form>

      <p v-if="session.state.status === 'error'" class="notice" data-tone="danger" data-testid="sign-in-unreachable">
        {{ session.state.error }}
      </p>

      <p class="login__where">
        Looking only for the network status? <RouterLink to="/status" data-testid="sign-in-status-link">The public status page</RouterLink>
        needs no account.
      </p>
    </div>
  </section>
</template>

<style scoped>
.login {
  display: grid;
  justify-items: center;
  padding-top: clamp(24px, 8vh, 96px);
}

.login__card {
  width: min(460px, 100%);
  padding: 30px 30px 24px;
}

.login__intro {
  display: grid;
  gap: 8px;
  margin-bottom: 22px;
}

.login__form {
  display: grid;
  gap: 14px;
}

.login__error {
  min-height: 21px;
  margin: 0;
  color: var(--oc-danger);
  font-size: 13.5px;
}

.login__where {
  margin-top: 18px;
  color: var(--oc-muted);
  font-size: 13.5px;
}
</style>

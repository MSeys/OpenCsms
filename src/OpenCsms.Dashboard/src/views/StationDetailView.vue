<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import {
  ApiProblem,
  getStation,
  listInvoices,
  listStationSessions,
  remoteStart,
  remoteStop,
  type ChargingSession,
  type Station
} from "../api";
import { session } from "../session";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import StatusBadge from "../components/StatusBadge.vue";
import { formatDateTime, formatKwh } from "../format";

const props = defineProps<{ stationId: string }>();

const station = ref<Station | null>(null);
const sessions = ref<ChargingSession[]>([]);
const invoiceBySession = ref(new Map<string, string>());
const loading = ref(true);
const loadError = ref<string | null>(null);

const idTag = ref("");
const connectorId = ref<number | null>(null);
const commandBusy = ref(false);
const commandResult = ref<string | null>(null);
const commandError = ref<string | null>(null);

const stopBusy = ref(false);
const stopResult = ref<string | null>(null);
const stopError = ref<string | null>(null);

const isOperator = computed(() => session.isOperator);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = null;
  try {
    const [loadedStation, loadedSessions, loadedInvoices] = await Promise.all([
      getStation(props.stationId),
      listStationSessions(props.stationId),
      listInvoices()
    ]);
    station.value = loadedStation;
    sessions.value = loadedSessions;
    invoiceBySession.value = new Map(loadedInvoices.map((invoice) => [invoice.sessionId, invoice.id]));
  } catch (cause) {
    loadError.value = cause instanceof ApiProblem ? cause.message : "The station could not be loaded.";
  } finally {
    loading.value = false;
  }
}

function invoiceFor(sessionId: string): string | null {
  return invoiceBySession.value.get(sessionId) ?? null;
}

async function startRemotely(): Promise<void> {
  commandBusy.value = true;
  commandResult.value = null;
  commandError.value = null;
  try {
    const answer = await remoteStart(props.stationId, idTag.value.trim(), connectorId.value);
    commandResult.value = `The charge point answered ${answer.status}.`;
  } catch (cause) {
    commandError.value = cause instanceof ApiProblem ? cause.message : "The remote start failed.";
  } finally {
    commandBusy.value = false;
  }
}

async function stopSession(sessionId: string): Promise<void> {
  stopBusy.value = true;
  stopResult.value = null;
  stopError.value = null;
  try {
    const answer = await remoteStop(sessionId);
    stopResult.value = `The charge point answered ${answer.status}.`;
  } catch (cause) {
    stopError.value = cause instanceof ApiProblem ? cause.message : "The remote stop failed.";
  } finally {
    stopBusy.value = false;
  }
}

onMounted(load);
</script>

<template>
  <section data-testid="station-page">
    <PageHeader
      :title="station?.name ?? 'Station'"
      description="The sessions this charge point has run, newest first."
      testid="station-title"
    >
      <template #actions>
        <RouterLink class="button button--quiet" to="/" data-testid="station-back">All stations</RouterLink>
      </template>
    </PageHeader>

    <LoadingBlock v-if="loading" :rows="4" label="Loading the station" />

    <div v-else-if="loadError" class="panel panel__body">
      <p class="notice" data-tone="danger" data-testid="station-error">{{ loadError }}</p>
      <button class="button" type="button" style="margin-top: 12px" data-testid="station-retry" @click="load">
        Retry
      </button>
    </div>

    <template v-else-if="station">
      <div class="panel panel__body">
        <dl class="facts">
          <div class="facts__item">
            <dt class="facts__label">Charge point</dt>
            <dd class="facts__value mono" data-testid="station-charge-point">{{ station.chargePointId }}</dd>
          </div>
          <div class="facts__item">
            <dt class="facts__label">Connectors</dt>
            <dd class="facts__value" data-testid="station-connectors">{{ station.connectorCount }}</dd>
          </div>
          <div class="facts__item">
            <dt class="facts__label">Last seen</dt>
            <dd class="facts__value" data-testid="station-last-seen">{{ formatDateTime(station.lastSeenAtUtc) }}</dd>
          </div>
        </dl>
      </div>

      <div v-if="isOperator" class="panel panel__body remote">
        <h2>Remote start</h2>
        <p class="muted">Ask the connected charge point to start a transaction from here.</p>
        <form class="remote__form" @submit.prevent="startRemotely">
          <div class="field">
            <label for="remote-id-tag">ID tag</label>
            <input id="remote-id-tag" v-model="idTag" class="input" type="text" required data-testid="remote-id-tag" />
          </div>
          <div class="field">
            <label for="remote-connector">Connector (optional)</label>
            <input id="remote-connector" v-model.number="connectorId" class="input" type="number" min="1" data-testid="remote-connector" />
          </div>
          <button class="button" type="submit" :disabled="commandBusy" data-testid="remote-start">
            Start remotely
          </button>
        </form>
        <p v-if="commandResult" class="notice" data-tone="success" data-testid="remote-start-result">
          {{ commandResult }}
        </p>
        <p v-if="commandError" class="notice" data-tone="danger" data-testid="remote-start-error">
          {{ commandError }}
        </p>
      </div>

      <div class="panel sessions">
        <div class="panel__header">
          <h2>Sessions</h2>
          <span class="muted" data-testid="sessions-count">{{ sessions.length }} session{{ sessions.length === 1 ? "" : "s" }}</span>
        </div>

        <EmptyState
          v-if="sessions.length === 0"
          title="No sessions yet"
          message="A session starts when this charge point sends its first StartTransaction."
          testid="sessions-empty"
        />

        <div v-else class="table-scroll">
          <table class="table" data-testid="sessions-table">
            <thead>
              <tr>
                <th scope="col">Connector</th>
                <th scope="col">Started</th>
                <th scope="col">Ended</th>
                <th scope="col">Energy</th>
                <th scope="col">State</th>
                <th scope="col">Invoice</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="session in sessions" :key="session.id" data-testid="session-row" :data-session-id="session.id">
                <td data-testid="session-connector">{{ session.connectorId }}</td>
                <td data-testid="session-started">{{ formatDateTime(session.startedAtUtc) }}</td>
                <td data-testid="session-ended">{{ session.endedAtUtc ? formatDateTime(session.endedAtUtc) : "—" }}</td>
                <td data-testid="session-energy">{{ formatKwh(session.energyKwh) }}</td>
                <td><StatusBadge :status="session.isOpen ? 'Open' : 'Ended'" testid="session-state" /></td>
                <td>
                  <button
                    v-if="isOperator && session.isOpen"
                    class="button button--quiet"
                    type="button"
                    :disabled="stopBusy"
                    data-testid="session-stop"
                    @click="stopSession(session.id)"
                  >
                    Stop
                  </button>
                  <RouterLink
                    v-else-if="invoiceFor(session.id)"
                    :to="`/invoices/${invoiceFor(session.id)}`"
                    data-testid="session-invoice"
                  >
                    Invoice
                  </RouterLink>
                  <span v-else class="muted" data-testid="session-invoice-none">—</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div v-if="stopResult || stopError" class="panel__body">
          <p v-if="stopResult" class="notice" data-tone="success" data-testid="session-stop-result">
            {{ stopResult }}
          </p>
          <p v-if="stopError" class="notice" data-tone="danger" data-testid="session-stop-error">
            {{ stopError }}
          </p>
        </div>
      </div>
    </template>
  </section>
</template>

<style scoped>
.sessions {
  margin-top: 18px;
}

.remote {
  margin-top: 18px;
}

.remote h2 {
  margin: 0 0 4px;
  font-size: 16px;
}

.remote__form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 12px;
  margin-top: 12px;
}
</style>

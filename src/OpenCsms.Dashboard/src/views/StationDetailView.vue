<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ApiProblem, getStation, listStationSessions, type ChargingSession, type Station } from "../api";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import StatusBadge from "../components/StatusBadge.vue";
import { formatDateTime, formatKwh } from "../format";

const props = defineProps<{ stationId: string }>();

const station = ref<Station | null>(null);
const sessions = ref<ChargingSession[]>([]);
const loading = ref(true);
const loadError = ref<string | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = null;
  try {
    const [loadedStation, loadedSessions] = await Promise.all([
      getStation(props.stationId),
      listStationSessions(props.stationId)
    ]);
    station.value = loadedStation;
    sessions.value = loadedSessions;
  } catch (cause) {
    loadError.value = cause instanceof ApiProblem ? cause.message : "The station could not be loaded.";
  } finally {
    loading.value = false;
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
              </tr>
            </thead>
            <tbody>
              <tr v-for="session in sessions" :key="session.id" data-testid="session-row" :data-session-id="session.id">
                <td data-testid="session-connector">{{ session.connectorId }}</td>
                <td data-testid="session-started">{{ formatDateTime(session.startedAtUtc) }}</td>
                <td data-testid="session-ended">{{ session.endedAtUtc ? formatDateTime(session.endedAtUtc) : "—" }}</td>
                <td data-testid="session-energy">{{ formatKwh(session.energyKwh) }}</td>
                <td><StatusBadge :status="session.isOpen ? 'Open' : 'Ended'" testid="session-state" /></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </template>
  </section>
</template>

<style scoped>
.sessions {
  margin-top: 18px;
}
</style>

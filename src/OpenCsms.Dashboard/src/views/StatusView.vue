<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ApiProblem, listPublicStations, type PublicStation } from "../api";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import StatusBadge from "../components/StatusBadge.vue";
import { formatDateTime } from "../format";

const stations = ref<PublicStation[]>([]);
const loading = ref(true);
const loadError = ref<string | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = null;
  try {
    stations.value = await listPublicStations();
  } catch (cause) {
    loadError.value = cause instanceof ApiProblem ? cause.message : "The network status could not be loaded.";
  } finally {
    loading.value = false;
  }
}

onMounted(load);
</script>

<template>
  <section data-testid="status-page">
    <PageHeader
      title="Network status"
      description="The charge points on this network and the last status each connector reported. No account needed."
      testid="status-title"
    />

    <LoadingBlock v-if="loading" :rows="4" label="Loading the network status" />

    <div v-else-if="loadError" class="panel panel__body">
      <p class="notice" data-tone="danger" data-testid="status-error">{{ loadError }}</p>
      <button class="button" type="button" style="margin-top: 12px" data-testid="status-retry" @click="load">
        Retry
      </button>
    </div>

    <div v-else-if="stations.length === 0" class="panel">
      <EmptyState
        title="No stations are registered"
        message="The network has no charge points yet."
        testid="status-empty"
      />
    </div>

    <div v-else class="status-grid">
      <article
        v-for="station in stations"
        :key="station.id"
        class="panel status-card"
        data-testid="status-station"
        :data-station-id="station.id"
      >
        <header class="status-card__header">
          <h2 data-testid="status-station-name">{{ station.name }}</h2>
          <span class="mono muted" data-testid="status-station-charge-point">{{ station.chargePointId }}</span>
        </header>
        <p class="muted status-card__seen" data-testid="status-station-last-seen">
          Last seen {{ formatDateTime(station.lastSeenAtUtc) }}
        </p>

        <ul v-if="station.connectors.length" class="status-card__connectors">
          <li v-for="connector in station.connectors" :key="connector.connectorId" data-testid="status-connector">
            <span class="mono muted">#{{ connector.connectorId }}</span>
            <StatusBadge :status="connector.status" testid="status-connector-state" />
          </li>
        </ul>
        <p v-else class="muted" data-testid="status-connector-none">No connector status reported yet.</p>
      </article>
    </div>
  </section>
</template>

<style scoped>
.status-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(290px, 1fr));
  gap: 16px;
}

.status-card {
  padding: 18px;
}

.status-card__header {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 4px 12px;
}

.status-card__seen {
  margin-top: 4px;
  font-size: 13.5px;
}

.status-card__connectors {
  display: grid;
  gap: 8px;
  margin: 14px 0 0;
  padding: 0;
  list-style: none;
}

.status-card__connectors li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-top: 1px solid var(--oc-line-soft);
  padding-top: 8px;
}
</style>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ApiProblem, listStations, type Station } from "../api";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import { formatDateTime } from "../format";

const stations = ref<Station[]>([]);
const loading = ref(true);
const loadError = ref<string | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = null;
  try {
    stations.value = await listStations();
  } catch (cause) {
    loadError.value = cause instanceof ApiProblem ? cause.message : "The station list could not be loaded.";
  } finally {
    loading.value = false;
  }
}

onMounted(load);
</script>

<template>
  <section data-testid="stations-page">
    <PageHeader
      eyebrow="Network"
      title="Charging stations"
      description="Every charge point this operator owns, and when each was last seen."
      testid="stations-title"
    />

    <div class="panel">
      <div class="panel__header">
        <h2>All stations</h2>
        <span class="muted" data-testid="stations-count">{{ stations.length }} station{{ stations.length === 1 ? "" : "s" }}</span>
      </div>

      <LoadingBlock v-if="loading" :rows="4" label="Loading stations" />

      <div v-else-if="loadError" class="panel__body">
        <p class="notice" data-tone="danger" data-testid="stations-error">{{ loadError }}</p>
        <button class="button" type="button" style="margin-top: 12px" data-testid="stations-retry" @click="load">
          Retry
        </button>
      </div>

      <EmptyState
        v-else-if="stations.length === 0"
        title="No stations yet"
        message="A station is a charge point registered for this operator. Register one through the API."
        testid="stations-empty"
      />

      <div v-else class="table-scroll">
        <table class="table" data-testid="stations-table">
          <thead>
            <tr>
              <th scope="col">Station</th>
              <th scope="col">Charge point</th>
              <th scope="col">Connectors</th>
              <th scope="col">Last seen</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="station in stations" :key="station.id" data-testid="station-row" :data-station-id="station.id">
              <td>
                <RouterLink :to="`/stations/${station.id}`" data-testid="station-link">{{ station.name }}</RouterLink>
              </td>
              <td class="mono" data-testid="station-charge-point">{{ station.chargePointId }}</td>
              <td data-testid="station-connectors">{{ station.connectorCount }}</td>
              <td class="muted" data-testid="station-last-seen">{{ formatDateTime(station.lastSeenAtUtc) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ApiProblem, listTariffs, type Tariff } from "../api";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import { formatDuration, formatMoney } from "../format";

const tariffs = ref<Tariff[]>([]);
const loading = ref(true);
const loadError = ref<string | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = null;
  try {
    tariffs.value = await listTariffs();
  } catch (cause) {
    loadError.value = cause instanceof ApiProblem ? cause.message : "The tariff list could not be loaded.";
  } finally {
    loading.value = false;
  }
}

onMounted(load);
</script>

<template>
  <section data-testid="tariffs-page">
    <PageHeader
      title="Tariffs"
      description="What this operator charges per kilowatt-hour, per session start and per idle hour."
      testid="tariffs-title"
    />

    <div class="panel">
      <div class="panel__header">
        <h2>All tariffs</h2>
        <span class="muted" data-testid="tariffs-count">{{ tariffs.length }} tariff{{ tariffs.length === 1 ? "" : "s" }}</span>
      </div>

      <LoadingBlock v-if="loading" :rows="4" label="Loading tariffs" />

      <div v-else-if="loadError" class="panel__body">
        <p class="notice" data-tone="danger" data-testid="tariffs-error">{{ loadError }}</p>
        <button class="button" type="button" style="margin-top: 12px" data-testid="tariffs-retry" @click="load">
          Retry
        </button>
      </div>

      <EmptyState
        v-else-if="tariffs.length === 0"
        title="No tariffs yet"
        message="A station bills against a tariff; register one through the API first."
        testid="tariffs-empty"
      />

      <div v-else class="table-scroll">
        <table class="table" data-testid="tariffs-table">
          <thead>
            <tr>
              <th scope="col">Tariff</th>
              <th scope="col">Energy</th>
              <th scope="col">Start fee</th>
              <th scope="col">Idle fee</th>
              <th scope="col">Idle grace</th>
              <th scope="col">Currency</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="tariff in tariffs" :key="tariff.id" data-testid="tariff-row" :data-tariff-id="tariff.id">
              <td data-testid="tariff-name">{{ tariff.name }}</td>
              <td data-testid="tariff-energy">{{ formatMoney(tariff.energyPricePerKwh, tariff.currency) }}</td>
              <td data-testid="tariff-start-fee">{{ formatMoney(tariff.startFee, tariff.currency) }}</td>
              <td data-testid="tariff-idle-fee">{{ formatMoney(tariff.idleFeePerHour, tariff.currency) }}</td>
              <td data-testid="tariff-grace">{{ formatDuration(tariff.idleGracePeriod) }}</td>
              <td class="mono" data-testid="tariff-currency">{{ tariff.currency }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ApiProblem, listTariffs, updateTariff, type Tariff } from "../api";
import { session } from "../session";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import { formatDuration, formatMoney } from "../format";

const tariffs = ref<Tariff[]>([]);
const loading = ref(true);
const loadError = ref<string | null>(null);

const editingId = ref<string | null>(null);
const energyPrice = ref(0);
const startFee = ref(0);
const idleFee = ref(0);
const graceMinutes = ref(0);
const saving = ref(false);
const saveError = ref<string | null>(null);

const isOperator = computed(() => session.isOperator);

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

/** A .NET "HH:mm:ss" grace period as the whole minutes the form edits. */
function graceToMinutes(value: string): number {
  const [hours = "0", minutes = "0"] = value.split(":");
  return Number(hours) * 60 + Number(minutes);
}

function startEditing(tariff: Tariff): void {
  editingId.value = tariff.id;
  energyPrice.value = tariff.energyPricePerKwh;
  startFee.value = tariff.startFee;
  idleFee.value = tariff.idleFeePerHour;
  graceMinutes.value = graceToMinutes(tariff.idleGracePeriod);
  saveError.value = null;
}

function cancelEditing(): void {
  editingId.value = null;
  saveError.value = null;
}

async function save(): Promise<void> {
  if (!editingId.value) {
    return;
  }

  saving.value = true;
  saveError.value = null;
  try {
    const totalMinutes = Math.max(0, Math.round(graceMinutes.value));
    const period = `${String(Math.floor(totalMinutes / 60)).padStart(2, "0")}:${String(totalMinutes % 60).padStart(2, "0")}:00`;
    const updated = await updateTariff(editingId.value, {
      energyPricePerKwh: energyPrice.value,
      startFee: startFee.value,
      idleFeePerHour: idleFee.value,
      idleGracePeriod: period
    });
    tariffs.value = tariffs.value.map((tariff) => (tariff.id === updated.id ? updated : tariff));
    editingId.value = null;
  } catch (cause) {
    saveError.value = cause instanceof ApiProblem ? cause.message : "The tariff could not be saved.";
  } finally {
    saving.value = false;
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
              <th v-if="isOperator" scope="col"><span class="visually-hidden">Actions</span></th>
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
              <td v-if="isOperator">
                <button
                  class="button button--quiet"
                  type="button"
                  data-testid="tariff-edit"
                  @click="startEditing(tariff)"
                >
                  Edit
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <p v-if="!isOperator && tariffs.length > 0" class="panel__body muted" data-testid="tariffs-readonly">
        Tariffs are read-only for the viewer role; an operator admin changes them.
      </p>

      <form v-if="isOperator && editingId" class="panel__body tariff-form" data-testid="tariff-form" @submit.prevent="save">
        <h2>Reprice the tariff</h2>
        <p class="muted">Sessions billed from now on use these prices; stored invoices keep theirs.</p>
        <div class="tariff-form__fields">
          <div class="field">
            <label for="tariff-energy-input">Energy price</label>
            <input
              id="tariff-energy-input"
              v-model.number="energyPrice"
              class="input"
              type="number"
              min="0"
              step="0.01"
              required
              data-testid="tariff-energy-input"
            />
          </div>
          <div class="field">
            <label for="tariff-start-input">Start fee</label>
            <input
              id="tariff-start-input"
              v-model.number="startFee"
              class="input"
              type="number"
              min="0"
              step="0.01"
              required
              data-testid="tariff-start-input"
            />
          </div>
          <div class="field">
            <label for="tariff-idle-input">Idle fee per hour</label>
            <input
              id="tariff-idle-input"
              v-model.number="idleFee"
              class="input"
              type="number"
              min="0"
              step="0.01"
              required
              data-testid="tariff-idle-input"
            />
          </div>
          <div class="field">
            <label for="tariff-grace-input">Idle grace (minutes)</label>
            <input
              id="tariff-grace-input"
              v-model.number="graceMinutes"
              class="input"
              type="number"
              min="0"
              step="1"
              required
              data-testid="tariff-grace-input"
            />
          </div>
        </div>
        <p v-if="saveError" class="notice" data-tone="danger" data-testid="tariff-form-error">{{ saveError }}</p>
        <div class="tariff-form__actions">
          <button class="button button--primary" type="submit" :disabled="saving" data-testid="tariff-save">
            Save prices
          </button>
          <button class="button button--quiet" type="button" data-testid="tariff-cancel" @click="cancelEditing">
            Cancel
          </button>
        </div>
      </form>
    </div>
  </section>
</template>

<style scoped>
.tariff-form {
  border-top: 1px solid var(--oc-line);
}

.tariff-form h2 {
  margin: 0 0 4px;
  font-size: 16px;
}

.tariff-form__fields {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 12px;
  margin-top: 12px;
}

.tariff-form__actions {
  display: flex;
  gap: 10px;
  margin-top: 14px;
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
}
</style>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ApiProblem, downloadInvoiceExport, listInvoices, type Invoice } from "../api";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import { formatDateTime, formatMoney } from "../format";

const invoices = ref<Invoice[]>([]);
const loading = ref(true);
const loadError = ref<string | null>(null);

const exportMonth = ref(new Date().toISOString().slice(0, 7));
const exportBusy = ref(false);
const exportError = ref<string | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = null;
  try {
    invoices.value = await listInvoices();
  } catch (cause) {
    loadError.value = cause instanceof ApiProblem ? cause.message : "The invoice list could not be loaded.";
  } finally {
    loading.value = false;
  }
}

async function downloadExport(): Promise<void> {
  const month = exportMonth.value.trim();
  if (!month) {
    exportError.value = "Enter the month to export as YYYY-MM.";
    return;
  }

  exportBusy.value = true;
  exportError.value = null;
  try {
    await downloadInvoiceExport(month);
  } catch (cause) {
    exportError.value = cause instanceof ApiProblem ? cause.message : "The export could not be downloaded.";
  } finally {
    exportBusy.value = false;
  }
}

onMounted(load);
</script>

<template>
  <section data-testid="invoices-page">
    <PageHeader
      eyebrow="Billing"
      title="Invoices"
      description="What the billing worker issued for this operator's ended sessions, newest first."
      testid="invoices-title"
    />

    <div class="panel export">
      <div class="panel__header">
        <h2>Monthly export</h2>
      </div>
      <div class="panel__body">
        <p class="muted">One spreadsheet per month: every invoice this operator's network issued in it.</p>
        <form class="export__form" @submit.prevent="downloadExport">
          <div class="field">
            <label for="export-month">Month (YYYY-MM)</label>
            <input
              id="export-month"
              v-model="exportMonth"
              class="input"
              type="month"
              required
              data-testid="export-month"
            />
          </div>
          <button class="button" type="submit" :disabled="exportBusy" data-testid="export-download">
            Download .xlsx
          </button>
        </form>
        <p v-if="exportError" class="notice" data-tone="danger" data-testid="export-error">{{ exportError }}</p>
      </div>
    </div>

    <div class="panel">
      <div class="panel__header">
        <h2>All invoices</h2>
        <span class="muted" data-testid="invoices-count">{{ invoices.length }} invoice{{ invoices.length === 1 ? "" : "s" }}</span>
      </div>

      <LoadingBlock v-if="loading" :rows="4" label="Loading invoices" />

      <div v-else-if="loadError" class="panel__body">
        <p class="notice" data-tone="danger" data-testid="invoices-error">{{ loadError }}</p>
        <button class="button" type="button" style="margin-top: 12px" data-testid="invoices-retry" @click="load">
          Retry
        </button>
      </div>

      <EmptyState
        v-else-if="invoices.length === 0"
        title="No invoices yet"
        message="An invoice appears here when an ended session has been billed."
        testid="invoices-empty"
      />

      <div v-else class="table-scroll">
        <table class="table" data-testid="invoices-table">
          <thead>
            <tr>
              <th scope="col">Issued</th>
              <th scope="col">Session</th>
              <th scope="col">Energy</th>
              <th scope="col">Total</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="invoice in invoices" :key="invoice.id" data-testid="invoice-row" :data-invoice-id="invoice.id">
              <td>
                <RouterLink :to="`/invoices/${invoice.id}`" data-testid="invoice-link">
                  {{ formatDateTime(invoice.issuedAtUtc) }}
                </RouterLink>
              </td>
              <td class="mono muted" data-testid="invoice-session">{{ invoice.sessionId }}</td>
              <td data-testid="invoice-energy">{{ invoice.energyKwh }} kWh</td>
              <td data-testid="invoice-total">{{ formatMoney(invoice.total, invoice.currency) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </section>
</template>

<style scoped>
.export {
  margin-bottom: 18px;
}

.export h2 {
  margin: 0;
  font-size: 16px;
}

.export__form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 12px;
  margin-top: 12px;
}
</style>

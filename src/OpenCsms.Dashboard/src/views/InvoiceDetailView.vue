<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ApiProblem, getInvoice, type Invoice } from "../api";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import { formatDateTime, formatKwh, formatMoney } from "../format";

const props = defineProps<{ invoiceId: string }>();

const invoice = ref<Invoice | null>(null);
const loading = ref(true);
const loadError = ref<string | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = null;
  try {
    invoice.value = await getInvoice(props.invoiceId);
  } catch (cause) {
    loadError.value = cause instanceof ApiProblem ? cause.message : "The invoice could not be loaded.";
  } finally {
    loading.value = false;
  }
}

onMounted(load);
</script>

<template>
  <section data-testid="invoice-page">
    <PageHeader
      eyebrow="Invoice"
      :title="invoice ? formatMoney(invoice.total, invoice.currency) : 'Invoice'"
      description="Energy, the start fee and any idle fee, as the billing worker calculated them."
      testid="invoice-title"
    >
      <template #actions>
        <RouterLink class="button" to="/invoices" data-testid="invoice-back"><span aria-hidden="true">←</span> All invoices</RouterLink>
      </template>
    </PageHeader>

    <LoadingBlock v-if="loading" :rows="4" label="Loading the invoice" />

    <div v-else-if="loadError" class="panel panel__body">
      <p class="notice" data-tone="danger" data-testid="invoice-error">{{ loadError }}</p>
      <button class="button" type="button" style="margin-top: 12px" data-testid="invoice-retry" @click="load">
        Retry
      </button>
    </div>

    <div v-else-if="invoice" class="panel panel__body">
      <dl class="facts">
        <div class="facts__item">
          <dt class="facts__label">Issued</dt>
          <dd class="facts__value" data-testid="invoice-issued">{{ formatDateTime(invoice.issuedAtUtc) }}</dd>
        </div>
        <div class="facts__item">
          <dt class="facts__label">Session</dt>
          <dd class="facts__value mono" data-testid="invoice-session">{{ invoice.sessionId }}</dd>
        </div>
        <div class="facts__item">
          <dt class="facts__label">Energy</dt>
          <dd class="facts__value" data-testid="invoice-energy">{{ formatKwh(invoice.energyKwh) }}</dd>
        </div>
        <div class="facts__item">
          <dt class="facts__label">Energy amount</dt>
          <dd class="facts__value" data-testid="invoice-energy-amount">{{ formatMoney(invoice.energyAmount, invoice.currency) }}</dd>
        </div>
        <div class="facts__item">
          <dt class="facts__label">Start fee</dt>
          <dd class="facts__value" data-testid="invoice-start-fee">{{ formatMoney(invoice.startFeeAmount, invoice.currency) }}</dd>
        </div>
        <div class="facts__item">
          <dt class="facts__label">Idle time</dt>
          <dd class="facts__value" data-testid="invoice-idle-hours">{{ invoice.idleHours }} started hour{{ invoice.idleHours === 1 ? "" : "s" }}</dd>
        </div>
        <div class="facts__item">
          <dt class="facts__label">Idle fee</dt>
          <dd class="facts__value" data-testid="invoice-idle-fee">{{ formatMoney(invoice.idleFeeAmount, invoice.currency) }}</dd>
        </div>
        <div class="facts__item">
          <dt class="facts__label">Total</dt>
          <dd class="facts__value" data-testid="invoice-total">{{ formatMoney(invoice.total, invoice.currency) }}</dd>
        </div>
      </dl>
    </div>
  </section>
</template>

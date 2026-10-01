<script setup lang="ts">
defineProps<{
  title: string;
  /** A short section label above the title, in ProtoTest's mono eyebrow style. */
  eyebrow?: string;
  description?: string;
  testid?: string;
}>();
</script>

<template>
  <header class="page-header">
    <div class="page-header__text">
      <p v-if="eyebrow" class="eyebrow">{{ eyebrow }}</p>
      <h1 :data-testid="testid">{{ title }}</h1>
      <p v-if="description" class="muted">{{ description }}</p>
    </div>
    <div v-if="$slots.actions" class="page-header__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<style scoped>
.page-header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px 20px;
  margin-bottom: 20px;
}

.page-header__text {
  max-width: 62ch;
}

.page-header__text .muted {
  margin-top: 5px;
}

.page-header__text .eyebrow {
  margin-bottom: 6px;
}

.page-header__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
</style>

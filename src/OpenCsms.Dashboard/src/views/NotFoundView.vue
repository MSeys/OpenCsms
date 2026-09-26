<script setup lang="ts">
</script>

<template>
  <section class="missing" data-testid="not-found">
    <h1>That screen does not exist</h1>
    <p class="muted">The address you opened is not part of the OpenCSMS dashboard.</p>
    <RouterLink class="button" to="/" data-testid="not-found-home">Back to the stations</RouterLink>
  </section>
</template>

<style scoped>
.missing {
  display: grid;
  justify-items: start;
  gap: 10px;
  padding-top: 40px;
}

.missing .button {
  margin-top: 8px;
}
</style>

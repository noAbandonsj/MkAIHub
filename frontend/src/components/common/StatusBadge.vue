<script setup lang="ts">
import type { StatusTone } from '@/utils/status'

defineProps<{
  label: string
  tone: StatusTone
}>()
</script>

<template>
  <span class="status-pill" :class="`is-${tone}`">{{ label }}</span>
</template>

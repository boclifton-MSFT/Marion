<template>
  <UPageSection
    title="Platform status"
    description="Live application and dependency status through the same-origin web boundary."
  >
    <div
      v-if="pending"
      class="grid gap-4 lg:grid-cols-2"
    >
      <USkeleton class="h-48" />
      <USkeleton class="h-48" />
    </div>

    <UAlert
      v-else-if="error"
      color="error"
      variant="subtle"
      title="Status unavailable"
      description="The platform status service could not be reached."
    />

    <div
      v-else-if="status"
      class="grid gap-4 lg:grid-cols-2"
    >
      <UCard>
        <template #header>
          <div class="flex items-center justify-between gap-4">
            <h3 class="font-semibold">
              Application
            </h3>
            <UBadge
              color="success"
              variant="subtle"
            >
              Online
            </UBadge>
          </div>
        </template>

        <dl class="space-y-3 text-sm">
          <div class="flex justify-between gap-4">
            <dt class="text-muted">
              Name
            </dt>
            <dd class="text-right font-medium">
              {{ status.info.applicationName }}
            </dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted">
              Version
            </dt>
            <dd class="text-right font-medium">
              {{ status.info.version }}
            </dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted">
              Environment
            </dt>
            <dd class="text-right font-medium">
              {{ status.info.environment }}
            </dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted">
              Checked
            </dt>
            <dd class="text-right font-medium">
              {{ formatUtc(status.info.utcTime) }}
            </dd>
          </div>
        </dl>
      </UCard>

      <UCard>
        <template #header>
          <h3 class="font-semibold">
            Dependencies
          </h3>
        </template>

        <ul class="divide-y divide-default">
          <li
            v-for="dependency in status.dependencies.dependencies"
            :key="dependency.name"
            class="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0"
          >
            <span class="font-medium">{{ dependency.name }}</span>
            <UBadge
              :color="statusColor(dependency.status)"
              variant="subtle"
            >
              {{ dependency.status }}
            </UBadge>
          </li>
        </ul>
      </UCard>
    </div>
  </UPageSection>
</template>

<script setup lang="ts">
import type {
  DependencyState,
  SystemDependenciesResponse,
  SystemInfoResponse
} from '~/types'

type SystemStatus = {
  info: SystemInfoResponse
  dependencies: SystemDependenciesResponse
}

const { get } = useApi()
const { data: status, pending, error } = await useAsyncData<SystemStatus>('system-status', async () => {
  const [info, dependencies] = await Promise.all([
    get<SystemInfoResponse>('/system/info'),
    get<SystemDependenciesResponse>('/system/dependencies')
  ])

  return { info, dependencies }
})

const statusColor = (state: DependencyState): 'success' | 'warning' | 'error' => {
  switch (state) {
    case 'Healthy':
      return 'success'
    case 'Degraded':
      return 'warning'
    default:
      return 'error'
  }
}

const formatUtc = (value: string) => new Date(value).toLocaleString()
</script>

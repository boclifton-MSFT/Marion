import type { ParsedContent } from '@nuxt/content'
import type { Avatar, Badge, Link } from '#ui/types'

export interface BlogPost extends ParsedContent {
  title: string
  description: string
  date: string
  image?: HTMLImageElement
  badge?: Badge
  authors?: ({
    name: string
    description?: string
    avatar: Avatar
  } & Link)[]
}

export interface SystemInfoResponse {
  applicationName: string
  version: string
  environment: string
  buildId?: string | null
  utcTime: string
}

export type DependencyState = 'Healthy' | 'Degraded' | 'Unavailable'

export interface SystemDependencyResponse {
  name: string
  status: DependencyState
}

export interface SystemDependenciesResponse {
  utcTime: string
  dependencies: SystemDependencyResponse[]
}

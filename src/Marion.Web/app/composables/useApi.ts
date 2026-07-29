import type { NitroFetchOptions, NitroFetchRequest } from 'nitropack'

type FetchOptions = NitroFetchOptions<NitroFetchRequest>
type RequestBody = FetchOptions['body']

export const useApi = () => {
  const apiBase = '/api'

  const apiFetch = async <T>(endpoint: string, options?: FetchOptions): Promise<T> => {
    const url = `${apiBase}${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}`
    if (import.meta.server) {
      return await useRequestFetch()(url, options) as T
    }

    return await $fetch(url, options) as T
  }

  const get = <T>(endpoint: string, options?: Omit<FetchOptions, 'method'>) => {
    return apiFetch<T>(endpoint, { ...options, method: 'GET' })
  }

  const post = <T>(endpoint: string, body?: RequestBody, options?: Omit<FetchOptions, 'method' | 'body'>) => {
    return apiFetch<T>(endpoint, {
      ...options,
      method: 'POST',
      body
    })
  }

  const put = <T>(endpoint: string, body?: RequestBody, options?: Omit<FetchOptions, 'method' | 'body'>) => {
    return apiFetch<T>(endpoint, {
      ...options,
      method: 'PUT',
      body
    })
  }

  const del = <T>(endpoint: string, options?: Omit<FetchOptions, 'method'>) => {
    return apiFetch<T>(endpoint, { ...options, method: 'DELETE' })
  }

  return {
    apiBase,
    fetch: apiFetch,
    get,
    post,
    put,
    del
  }
}

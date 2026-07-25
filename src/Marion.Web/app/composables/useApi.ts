import type { NitroFetchOptions, NitroFetchRequest } from 'nitropack'

type FetchOptions = NitroFetchOptions<NitroFetchRequest>
type RequestBody = FetchOptions['body']

export const useApi = () => {
  const config = useRuntimeConfig()
  const apiBase = config.public.apiBase

  const apiFetch = async <T>(endpoint: string, options?: FetchOptions): Promise<T> => {
    const url = `${apiBase}${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}`
    const response = await $fetch<T>(url, options)
    return response
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

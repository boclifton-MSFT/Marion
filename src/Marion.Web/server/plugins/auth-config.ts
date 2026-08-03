import { authConfigurationIssues } from '../utils/auth/runtime'

export default defineNitroPlugin(() => {
  if (process.env.NODE_ENV !== 'production') {
    return
  }

  const issues = authConfigurationIssues(useRuntimeConfig(), { production: true })
  if (issues.length > 0) {
    throw new Error(`Authentication runtime configuration is invalid: ${issues.join('; ')}`)
  }
})

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  modules: [
    '@nuxt/eslint',
    '@nuxt/image',
    '@nuxt/ui',
    '@nuxt/content',
    '@vueuse/nuxt',
    'nuxt-og-image'
  ],

  devtools: {
    enabled: true
  },

  css: ['~/assets/css/main.css'],

  runtimeConfig: {
    apiBase: process.env.NUXT_API_BASE
      || process.env.services__apiservice_https_0
      || process.env.services__apiservice_http_0
      || 'http://localhost:5435',
    oauth: {
      oidc: {
        issuer: '',
        clientId: '',
        clientSecret: '',
        redirectUri: ''
      }
    },
    session: {
      name: '__Host-marion_session',
      password: '',
      maxAge: 60 * 60 * 8,
      sessionHeader: false,
      cookie: {
        secure: true,
        httpOnly: true,
        sameSite: 'lax',
        path: '/'
      }
    },
    authStore: {
      connectionString: process.env.NUXT_AUTH_STORE_CONNECTION_STRING
        || process.env.ConnectionStrings__mariondb
        || '',
      provisionSchema: process.env.NUXT_AUTH_STORE_PROVISION_SCHEMA === 'true'
    }
  },

  routeRules: {
    '/docs': { redirect: '/docs/getting-started', prerender: false },
    '/sw.js': { headers: { 'Cache-Control': 'no-cache' } }
  },

  compatibilityDate: '2024-07-11',

  nitro: {
    prerender: {
      routes: [
        '/'
      ],
      crawlLinks: true
    }
  },

  eslint: {
    config: {
      stylistic: {
        commaDangle: 'never',
        braceStyle: '1tbs'
      }
    }
  }
})

# GitHub Copilot Instructions

Marion is an AI-powered loan officer assistant using **Aspire** for cloud-native orchestration and **Nuxt UI** for the frontend. Follow these guidelines when assisting with development.

## Project Overview

- **Backend**: .NET 10 with ASP.NET Core Minimal APIs, orchestrated by Aspire 13.4
- **Frontend**: Nuxt 4 with Nuxt UI 4, Vue 3 Composition API, TypeScript
- **Package Manager**: pnpm (for frontend)
- **Architecture**: Microservices-style with service discovery and distributed tracing

## Code Style Guidelines

### C# / .NET

- Use **Minimal APIs** pattern for new endpoints (not controllers)
- Follow .NET naming conventions (PascalCase for public members, camelCase for locals)
- Use **records** for DTOs and immutable data structures
- Enable nullable reference types (`<Nullable>enable</Nullable>`)
- Use **top-level statements** in Program.cs
- Register services using `builder.AddServiceDefaults()` for Aspire integration
- Add health check endpoints for all services via `app.MapDefaultEndpoints()`

```csharp
// Preferred: Minimal API style
app.MapGet("/api/items", (IItemService service) => service.GetAllItems())
    .WithName("GetItems")
    .WithOpenApi();
```

### TypeScript / Vue / Nuxt

- Use **TypeScript** for all new code
- Use **Vue 3 Composition API** with `<script setup>` syntax
- Use **Nuxt UI components** (`<UButton>`, `<UCard>`, etc.) instead of raw HTML
- Use composables for reusable logic (place in `app/composables/`)
- Use the `useApi()` composable for backend API calls
- Define types in `app/types/index.d.ts`
- Use **Zod** for runtime validation of external data

```vue
<script setup lang="ts">
// Preferred: Composition API with script setup
const { get } = useApi()
const { data } = await useAsyncData('items', () => get<Item[]>('/api/items'))
</script>
```

### Content (Markdown)

- Place documentation in `content/1.docs/`
- Place blog posts in `content/3.blog/`
- Place changelog entries in `content/4.changelog/`
- Use YAML frontmatter for metadata
- Follow Nuxt Content conventions for file naming (numbered prefixes for ordering)

## Architecture Decisions

### Service Communication

- Services communicate via HTTP with automatic service discovery
- The frontend receives the API base URL via `NUXT_PUBLIC_API_BASE` environment variable
- Use the `useApi()` composable which handles the base URL configuration

### Aspire Integration

- All .NET services should reference `Marion.ServiceDefaults` for shared configuration
- Service defaults include: OpenTelemetry, health checks, resilience handlers, service discovery
- Register new services in `Marion.AppHost/AppHost.cs`

```csharp
// Adding a new service to Aspire
var newService = builder.AddProject<Projects.Marion_NewService>("newservice")
    .WithHttpHealthCheck("/health");
```

### State Management

- Use Nuxt's built-in state management (`useState`, `useAsyncData`)
- For complex state, consider Pinia (not currently installed)
- Prefer server-side data fetching with `useAsyncData` or `useFetch`

## File Naming Conventions

| Type | Location | Convention |
|------|----------|------------|
| Vue Pages | `app/pages/` | `kebab-case.vue` |
| Vue Components | `app/components/` | `PascalCase.vue` |
| Composables | `app/composables/` | `useCamelCase.ts` |
| TypeScript Types | `app/types/` | `index.d.ts` or `feature.d.ts` |
| API Endpoints | `Program.cs` | `/api/resource` pattern |
| Content Files | `content/` | `number.slug.md` |

## Common Tasks

### Adding a New API Endpoint

1. Add the endpoint in `Marion.ApiService/Program.cs`
2. Add OpenAPI metadata with `.WithName()` and `.WithOpenApi()`
3. Create a corresponding type in the frontend if needed

### Adding a New Page

1. Create a `.vue` file in `app/pages/`
2. Use a layout from `app/layouts/` (default, docs, or auth)
3. Add navigation in `AppHeader.vue` if needed

### Adding a New Component

1. Create in `app/components/` using PascalCase
2. Components are auto-imported by Nuxt
3. Use Nuxt UI primitives when possible

### Adding Content

1. Create Markdown file in appropriate `content/` subdirectory
2. Add YAML frontmatter with required fields
3. Content is automatically indexed and searchable

## Dependencies to Prefer

### Backend
- `Microsoft.AspNetCore.OpenApi` - OpenAPI/Swagger support
- Built-in .NET libraries over third-party when equivalent

### Frontend
- **Nuxt UI** components over custom styling
- **VueUse** composables for common utilities
- **Zod** for validation
- **Iconify** icons via `@iconify-json/*` packages

## Testing Guidelines

- Place .NET tests in a separate test project following `Marion.Tests` convention
- Use xUnit for .NET testing
- For frontend, use Vitest (not currently configured, add if needed)

## Performance Considerations

- Use `useAsyncData` for server-side data fetching
- Enable Nuxt's built-in prerendering for static pages
- Leverage Aspire's built-in caching when adding caching services
- Use `<LazyComponent>` prefix for components that should be lazy-loaded

## Security Notes

- Store secrets using .NET User Secrets in development
- Never commit sensitive configuration to the repository
- Use environment variables for production secrets
- API endpoints should validate input data

## Useful Commands

```bash
# Run full stack with Aspire
aspire start

# Frontend only
cd src/Marion.Web && pnpm dev

# Lint frontend
cd src/Marion.Web && pnpm lint

# Type check frontend
cd src/Marion.Web && pnpm typecheck
```

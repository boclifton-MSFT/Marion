using Microsoft.EntityFrameworkCore;

namespace Marion.ApiService.Infrastructure.Persistence;

public sealed class MarionDbContext(DbContextOptions<MarionDbContext> options)
    : DbContext(options);

using Xunit;

namespace Marion.ApiService.Tests;

/// <summary>
/// Each of these tests boots a full distributed application; running them concurrently starves
/// the container host and they time out waiting for resources to become healthy.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DistributedApplicationCollection
{
  public const string Name = "distributed-application";
}

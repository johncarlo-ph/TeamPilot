using TeamPilot.Application.Git;
using Xunit;

namespace TeamPilot.Application.Tests.Git;

public class GitRepositoryLockTests
{
    [Fact]
    public async Task AcquireAsync_WithSamePath_SerializesConcurrentHolders()
    {
        var path = Path.Combine("sandboxes", Guid.NewGuid().ToString());
        var concurrentHolders = 0;
        var maxConcurrentHolders = 0;
        var gate = new object();

        async Task RunAsync()
        {
            await using (await GitRepositoryLock.AcquireAsync(path))
            {
                lock (gate)
                {
                    concurrentHolders++;
                    maxConcurrentHolders = Math.Max(maxConcurrentHolders, concurrentHolders);
                }

                await Task.Delay(20);

                lock (gate)
                {
                    concurrentHolders--;
                }
            }
        }

        await Task.WhenAll(RunAsync(), RunAsync(), RunAsync());

        Assert.Equal(1, maxConcurrentHolders);
    }

    [Fact]
    public async Task AcquireAsync_WithDifferentPaths_RunsConcurrently()
    {
        var concurrentHolders = 0;
        var maxConcurrentHolders = 0;
        var gate = new object();

        async Task RunAsync(string path)
        {
            await using (await GitRepositoryLock.AcquireAsync(path))
            {
                lock (gate)
                {
                    concurrentHolders++;
                    maxConcurrentHolders = Math.Max(maxConcurrentHolders, concurrentHolders);
                }

                await Task.Delay(20);

                lock (gate)
                {
                    concurrentHolders--;
                }
            }
        }

        var pathA = Path.Combine("sandboxes", Guid.NewGuid().ToString());
        var pathB = Path.Combine("sandboxes", Guid.NewGuid().ToString());

        await Task.WhenAll(RunAsync(pathA), RunAsync(pathB));

        Assert.Equal(2, maxConcurrentHolders);
    }

    [Fact]
    public async Task AcquireAsync_IsCaseInsensitiveToPath()
    {
        var path = Path.Combine("Sandboxes", Guid.NewGuid().ToString());
        var concurrentHolders = 0;
        var maxConcurrentHolders = 0;
        var gate = new object();

        async Task RunAsync(string acquirePath)
        {
            await using (await GitRepositoryLock.AcquireAsync(acquirePath))
            {
                lock (gate)
                {
                    concurrentHolders++;
                    maxConcurrentHolders = Math.Max(maxConcurrentHolders, concurrentHolders);
                }

                await Task.Delay(20);

                lock (gate)
                {
                    concurrentHolders--;
                }
            }
        }

        // Same path, three different casings - all three should still serialize against one
        // underlying gate if the key comparison is truly case-insensitive.
        await Task.WhenAll(RunAsync(path), RunAsync(path.ToUpperInvariant()), RunAsync(path.ToLowerInvariant()));

        Assert.Equal(1, maxConcurrentHolders);
    }
}

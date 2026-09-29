using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace UnitTests.Authorization;

public sealed class PermissionAuthorizationPolicyProviderTests
{
    [Fact]
    public async Task ConcurrentPermissionLookups_DoNotMutateTheSharedAuthorizationOptions()
    {
        AuthorizationOptions options = new();
        PermissionAuthorizationPolicyProvider[] providers = Enumerable.Range(0, 8)
            .Select(_ => new PermissionAuthorizationPolicyProvider(Options.Create(options)))
            .ToArray();

        AuthorizationPolicy?[] policies = await Task.WhenAll(
            Enumerable.Range(0, 1000)
                .Select(i => providers[i % providers.Length].GetPolicyAsync($"permission:{i % 50}")));

        policies.ShouldAllBe(policy => policy != null);
        policies[0]!.Requirements.OfType<PermissionRequirement>().Single().Permission
            .ShouldBe("permission:0");
        options.GetPolicy("permission:0").ShouldBeNull();
    }
}

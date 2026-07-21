using System.Reflection;
using Clawbot.SharedKernel.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

public sealed class ContentPlatformContractTests
{
    private const string ContractTypeName = "Clawbot.SharedKernel.Content.ContentPlatform";

    [Fact]
    public void Writable_platforms_are_exactly_facebook_zalo_and_instagram()
    {
        var contract = GetContractType();
        var property = contract.GetProperty("Writable", BindingFlags.Public | BindingFlags.Static);

        property.Should().NotBeNull("the shared contract must expose the one canonical writable set");
        var writable = property!.GetValue(null).Should().BeAssignableTo<IEnumerable<string>>().Subject;
        writable.Should().BeEquivalentTo("facebook", "zalo", "instagram");
    }

    [Theory]
    [InlineData("facebook", "facebook")]
    [InlineData(" ZALO ", "zalo")]
    [InlineData("Instagram", "instagram")]
    public void TryNormalizeWritable_trims_and_lowercases_canonical_values(string input, string expected)
    {
        var (success, normalized) = TryNormalizeWritable(input);

        success.Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tiktok")]
    [InlineData("youtube")]
    [InlineData("website")]
    [InlineData("meta")]
    [InlineData("fb")]
    public void TryNormalizeWritable_rejects_unknown_values_without_fallback_or_coercion(string? input)
    {
        var (success, normalized) = TryNormalizeWritable(input);

        success.Should().BeFalse();
        normalized.Should().BeNull();
    }

    private static (bool Success, string? Normalized) TryNormalizeWritable(string? input)
    {
        var contract = GetContractType();
        var method = contract.GetMethod("TryNormalizeWritable", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull("all backend boundaries need one shared normalization operation");

        object?[] args = [input, null];
        var success = method!.Invoke(null, args).Should().BeOfType<bool>().Subject;
        return (success, args[1] as string);
    }

    private static Type GetContractType()
    {
        var contract = typeof(IGoldenHourResolver).Assembly.GetType(ContractTypeName);
        contract.Should().NotBeNull("Phase 1 requires a shared backend canonical platform contract");
        return contract!;
    }
}

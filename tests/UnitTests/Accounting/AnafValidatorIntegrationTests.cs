using Application.Abstractions.Anaf;
using Domain.Accounting;
using Infrastructure.Accounting.Anaf;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Acceptanța B4: cele 3 XML-uri ale cazului Ion Popescu trec XSD-ul oficial și validatorul ANAF.
/// Rulează doar cu serviciul Java pornit și <c>RIDELANCE_ANAF_VALIDATOR_URL</c> setat (plus
/// <c>RIDELANCE_ANAF_VALIDATOR_TOKEN</c>, <c>RIDELANCE_ANAF_VALIDATOR_VERSION</c>); altfel nu face nimic.
/// </summary>
public sealed class AnafValidatorIntegrationTests
{
    private static readonly string? BaseUrl = Environment.GetEnvironmentVariable("RIDELANCE_ANAF_VALIDATOR_URL");

    [Theory]
    [InlineData(DeclarationType.D100, "v2-20220224", AnafSchemaCorrections.D100V2)]
    [InlineData(DeclarationType.D301, "v1-20200130", AnafSchemaCorrections.D301V1)]
    [InlineData(DeclarationType.D390, "v3-20210212", AnafSchemaCorrections.D390V3)]
    public async Task Ion_declarations_pass_the_anaf_validator(DeclarationType type, string schemaVersion, string xsd)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return;
        }

        var service = new AnafDeclarationXmlService();
        byte[] xml = service.Build(schemaVersion, AnafTestSupport.Input(type));
        service.ValidateSchema(xsd, xml).ShouldBeEmpty();

        using var http = new HttpClient();
        var client = new AnafValidatorClient(
            http,
            Options.Create(new AnafValidatorOptions
            {
                BaseUrl = BaseUrl,
                Token = Environment.GetEnvironmentVariable("RIDELANCE_ANAF_VALIDATOR_TOKEN"),
            }),
            NullLogger<AnafValidatorClient>.Instance);

        Result<AnafValidatorResult> result = await client.ValidateAsync(
            type,
            Environment.GetEnvironmentVariable("RIDELANCE_ANAF_VALIDATOR_VERSION") ?? "2026-09",
            xml,
            $"test-{type}",
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : null);
        result.Value.Errors.ShouldBeEmpty(result.Value.RawOutput);
        result.Value.Valid.ShouldBeTrue();
        result.Value.Pdf.ShouldNotBeNull().Take(4).ShouldBe("%PDF"u8.ToArray());
    }
}

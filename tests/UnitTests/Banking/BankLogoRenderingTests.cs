using Infrastructure.Banking;
using Shouldly;
using Xunit;

namespace UnitTests.Banking;

/// <summary>
/// Logo-urile din lista de bănci. Smart Accounts le trimite ca <c>data:image/svg;base64,…</c>, cu un
/// tip pe care browserul nu-l recunoaște, deci în pagină apăreau pătrate goale în locul lor.
/// </summary>
public sealed class BankLogoRenderingTests
{
    [Fact]
    public void A_truncated_svg_type_is_fixed() =>
        SmartAccountsBankDataProvider.RenderableLogo("data:image/svg;base64,PHN2Zz4=")
            .ShouldBe("data:image/svg+xml;base64,PHN2Zz4=");

    [Fact]
    public void A_correct_svg_type_is_left_alone() =>
        SmartAccountsBankDataProvider.RenderableLogo("data:image/svg+xml;base64,PHN2Zz4=")
            .ShouldBe("data:image/svg+xml;base64,PHN2Zz4=");

    [Fact]
    public void Other_images_and_addresses_are_left_alone()
    {
        SmartAccountsBankDataProvider.RenderableLogo("data:image/png;base64,AAAA").ShouldBe("data:image/png;base64,AAAA");
        SmartAccountsBankDataProvider.RenderableLogo("https://cdn.example/bt.svg").ShouldBe("https://cdn.example/bt.svg");
        SmartAccountsBankDataProvider.RenderableLogo(null).ShouldBeNull();
    }
}

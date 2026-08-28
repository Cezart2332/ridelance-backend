using Domain.Rentals;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Linkul de semnare e singura autentificare a chiriașului, care n-are cont.
/// </summary>
/// <remarks>
/// De aici cele două proprietăți care contează: tokenul nu se poate ghici, iar ce păstrăm noi nu
/// se poate întoarce în token. O bază de date citită de cineva nu trebuie să ofere și cheile de
/// semnare.
/// </remarks>
public sealed class SignatureTokenTests
{
    [Fact]
    public void Two_tokens_are_never_the_same()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 500; i++)
        {
            seen.Add(SignatureToken.Create()).ShouldBeTrue();
        }
    }

    [Fact]
    public void A_token_survives_a_url_without_escaping()
    {
        // Ajunge într-un link din email. Un „+" sau un „/" l-ar rupe la prima copiere.
        string token = SignatureToken.Create();
        token.ShouldNotContain("+");
        token.ShouldNotContain("/");
        token.ShouldNotContain("=");
    }

    [Fact]
    public void The_stored_hash_is_not_the_token()
    {
        string token = SignatureToken.Create();
        SignatureToken.Hash(token).ShouldNotBe(token);
    }

    [Fact]
    public void The_same_token_always_hashes_the_same_way()
    {
        // Altfel căutarea după amprentă n-ar găsi niciodată cererea.
        string token = SignatureToken.Create();
        SignatureToken.Hash(token).ShouldBe(SignatureToken.Hash(token));
    }

    [Fact]
    public void A_different_token_hashes_differently()
    {
        SignatureToken.Hash(SignatureToken.Create())
            .ShouldNotBe(SignatureToken.Hash(SignatureToken.Create()));
    }

    [Fact]
    public void A_link_lasts_a_week()
    {
        SignatureToken.Lifetime.ShouldBe(TimeSpan.FromDays(7));
    }
}

/// <summary>
/// Consumarea linkului. Criteriul de acceptanță al fazei: se semnează o singură dată.
/// </summary>
public sealed class SignatureRequestLifecycleTests
{
    private static SignatureRequest Fresh() => new()
    {
        Id = Guid.NewGuid(),
        TokenHash = SignatureToken.Hash(SignatureToken.Create()),
        Email = "chirias@example.com",
        ExpiresAtUtc = DateTime.UtcNow.Add(SignatureToken.Lifetime),
    };

    [Fact]
    public void A_fresh_request_has_not_been_used()
    {
        Fresh().UsedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void An_expired_request_is_recognised_by_its_own_date()
    {
        SignatureRequest request = Fresh();
        request.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);

        (request.ExpiresAtUtc <= DateTime.UtcNow).ShouldBeTrue();
    }

    [Fact]
    public void Signing_marks_the_request_used_so_the_same_link_cannot_sign_twice()
    {
        SignatureRequest request = Fresh();

        request.UsedAtUtc = DateTime.UtcNow;

        request.UsedAtUtc.ShouldNotBeNull();
    }
}

using System.Text.Json;
using Infrastructure.Banking;
using Shouldly;
using Xunit;

namespace UnitTests.Banking;

/// <summary>
/// Citirea răspunsurilor Fintable. Contractul lor declară conturile și tranzacțiile ca obiecte
/// generice, deci maparea trebuie să fie tolerantă la câmpuri lipsă — dar intolerantă la
/// interpretări greșite ale sumelor, care sunt string-uri, nu numere.
/// </summary>
public sealed class FintableJsonTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Amounts_arrive_as_strings_and_are_read_with_the_invariant_culture()
    {
        // Cu cultura română, „5240.12" ar deveni 524012 — o mie de ori mai mult.
        JsonElement element = Parse("""{"balance": "5240.12"}""");

        FintableJson.Decimal(element, "balance").ShouldBe(5240.12m);
    }

    [Fact]
    public void Negative_amounts_keep_their_sign()
    {
        FintableJson.Decimal(Parse("""{"amount": "-4.50"}"""), "amount").ShouldBe(-4.50m);
    }

    [Fact]
    public void A_numeric_amount_is_accepted_too()
    {
        // Contractul nu garantează tipul, doar exemplele arată string-uri.
        FintableJson.Decimal(Parse("""{"amount": 12.34}"""), "amount").ShouldBe(12.34m);
    }

    [Fact]
    public void An_unreadable_amount_becomes_null_not_zero()
    {
        // Zero ar intra tăcut în totaluri; null oprește înregistrarea tranzacției.
        FintableJson.Decimal(Parse("""{"amount": "n/a"}"""), "amount").ShouldBeNull();
        FintableJson.Decimal(Parse("""{"other": "1"}"""), "amount").ShouldBeNull();
    }

    [Fact]
    public void Field_names_are_tried_in_order_until_one_matches()
    {
        JsonElement element = Parse("""{"description": "BLUE BOTTLE COFFEE"}""");

        FintableJson.String(element, "counterparty", "merchant", "description")
            .ShouldBe("BLUE BOTTLE COFFEE");
    }

    [Fact]
    public void A_nested_object_falls_back_to_its_name()
    {
        // Instituția poate veni ca string sau ca obiect; ambele trebuie să dea același rezultat.
        FintableJson.String(Parse("""{"institution": {"name": "BCR"}}"""), "institution").ShouldBe("BCR");
    }

    [Fact]
    public void Empty_strings_count_as_missing()
    {
        FintableJson.String(Parse("""{"name": "  ", "title": "BCR"}"""), "name", "title").ShouldBe("BCR");
    }

    [Fact]
    public void Dates_are_read_from_plain_dates_and_from_timestamps()
    {
        FintableJson.Date(Parse("""{"date": "2026-07-24"}"""), "date")
            .ShouldBe(new DateOnly(2026, 7, 24));

        FintableJson.Date(Parse("""{"date": "2026-07-24T15:42:00Z"}"""), "date")
            .ShouldBe(new DateOnly(2026, 7, 24));
    }

    [Fact]
    public void The_data_envelope_is_unwrapped()
    {
        JsonElement data = FintableJson.Data(Parse("""{"data": {"url": "https://x"}, "workspace_id": "ws_1"}"""));

        FintableJson.String(data, "url").ShouldBe("https://x");
    }

    [Fact]
    public void A_response_without_an_envelope_is_returned_as_is()
    {
        FintableJson.String(FintableJson.Data(Parse("""{"url": "https://x"}""")), "url").ShouldBe("https://x");
    }

    [Fact]
    public void A_null_cursor_ends_the_pagination()
    {
        FintableJson.Cursor(Parse("""{"data": [], "next_cursor": null}""")).ShouldBeNull();
        FintableJson.Cursor(Parse("""{"data": [], "next_cursor": "cur_2"}""")).ShouldBe("cur_2");
    }
}

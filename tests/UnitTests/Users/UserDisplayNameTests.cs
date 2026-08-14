using Domain.Users;
using Shouldly;
using Xunit;

namespace UnitTests.Users;

/// <summary>
/// RL-05 — contul se creează fără nume, deci fiecare suprafață care afișa
/// „FirstName + LastName” putea ajunge să afișeze un spațiu gol. Fallbackul e testat aici,
/// fiindcă e singurul lucru care stă între asta și un email „Salut, ”.
/// </summary>
public class UserDisplayNameTests
{
    [Fact]
    public void Of_PrefersTheFullName()
    {
        UserDisplayName.Of(User("Ion", "Popescu", "ion.popescu@example.com"))
            .ShouldBe("Ion Popescu");
    }

    [Fact]
    public void Of_FallsBackToTheEmailLocalPart()
    {
        UserDisplayName.Of(User("", "", "ion.popescu@example.com"))
            .ShouldBe("ion.popescu");
    }

    [Theory]
    [InlineData("Ion", "", "Ion")]
    [InlineData("", "Popescu", "Popescu")]
    public void Of_AcceptsAPartialName(string firstName, string lastName, string expected)
    {
        UserDisplayName.Of(User(firstName, lastName, "sofer@example.com")).ShouldBe(expected);
    }

    [Fact]
    public void Of_FallsBackToAccountLabel_WhenThereIsNothingUsable()
    {
        UserDisplayName.Of(User("", "", "")).ShouldBe("Contul meu");
    }

    [Fact]
    public void Of_IgnoresWhitespaceOnlyNames()
    {
        // Nu doar null: coloanele sunt NOT NULL, deci golul arată ca „ ” după concatenare.
        UserDisplayName.Of(User("   ", "  ", "sofer@example.com")).ShouldBe("sofer");
    }

    [Fact]
    public void GreetingFor_UsesTheFirstNameAlone()
    {
        UserDisplayName.GreetingFor(User("Ion", "Popescu", "ion@example.com")).ShouldBe("Ion");
    }

    [Fact]
    public void GreetingFor_NeverGreetsAnEmptyString()
    {
        UserDisplayName.GreetingFor(User("", "", "ion@example.com")).ShouldBe("ion");
    }

    [Fact]
    public void IsMissing_IsTrueOnlyWhenNothingWasFilledIn()
    {
        UserDisplayName.IsMissing(User("", "", "ion@example.com")).ShouldBeTrue();
        UserDisplayName.IsMissing(User("  ", " ", "ion@example.com")).ShouldBeTrue();
        UserDisplayName.IsMissing(User("Ion", "", "ion@example.com")).ShouldBeFalse();
    }

    private static User User(string firstName, string lastName, string email) =>
        new() { Id = Guid.NewGuid(), FirstName = firstName, LastName = lastName, Email = email };
}

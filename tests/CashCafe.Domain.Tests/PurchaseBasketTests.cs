using CashCafe.Domain;
using CashCafe.Domain.Rules;
using FluentAssertions;
using Xunit;

namespace CashCafe.Domain.Tests;

public class PurchaseBasketTests
{
    private static readonly CafeSettings Settings = CafeSettings.Defaults;

    private static Student StudentWith(decimal kronor, bool active = true, Money? limit = null) => new()
    {
        Id = 41,
        FirstName = "Carl",
        LastName = "Jacobs",
        DisplayName = "Carl Jacobs",
        SearchName = "carl jacobs",
        Balance = Money.FromKronor(kronor),
        IsActive = active,
        CreditLimit = limit,
    };

    private static Item ItemWith(string name, decimal price, long id = 1) => new()
    {
        Id = id,
        Name = name,
        SearchName = SearchNormalizer.Normalize(name),
        Price = Money.FromKronor(price),
    };

    [Fact]
    public void A_new_basket_starts_with_one_empty_row()
    {
        var basket = new PurchaseBasket(Settings);

        basket.Lines.Should().HaveCount(1);
        basket.Lines[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Filling_a_row_opens_the_next_one_by_itself()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SetLine(1, BasketLine.For(1, ItemWith("Toast", 10)));

        basket.Lines.Should().HaveCount(2);
        basket.Lines[1].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Rows_stop_opening_at_the_maximum_of_ten()
    {
        var basket = new PurchaseBasket(Settings);

        for (var i = 1; i <= 10; i++)
            basket.SetLine(i, BasketLine.For(i, ItemWith("Toast", 10)));

        basket.Lines.Should().HaveCount(10);
        basket.IsFull.Should().BeTrue();
    }

    [Fact]
    public void Removing_a_row_renumbers_the_rest()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SetLine(1, BasketLine.For(1, ItemWith("Toast", 10, id: 1)));
        basket.SetLine(2, BasketLine.For(2, ItemWith("Juice", 15, id: 2)));

        basket.RemoveLine(1);

        basket.Lines[0].ItemName.Should().Be("Juice");
        basket.Lines[0].LineNo.Should().Be(1);
    }

    [Fact]
    public void The_total_is_the_sum_of_the_line_totals()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SetLine(1, BasketLine.For(1, ItemWith("Toast", 10, id: 1)));
        basket.SetLine(2, BasketLine.For(2, ItemWith("Juice", 15, id: 2), quantity: 2));

        basket.Total.Should().Be(Money.FromKronor(40));
        basket.ItemCount.Should().Be(3);
    }

    [Fact]
    public void Execute_is_blocked_until_a_student_is_chosen()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SetLine(1, BasketLine.For(1, ItemWith("Toast", 10)));

        var validation = basket.Validate();

        validation.CanExecute.Should().BeFalse();
        validation.Problem.Should().Be(BasketProblem.NoStudent);
    }

    [Fact]
    public void Execute_is_blocked_with_no_items()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SelectStudent(StudentWith(50));

        basket.Validate().Problem.Should().Be(BasketProblem.NoItems);
    }

    [Fact]
    public void Execute_is_blocked_for_a_deactivated_student()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SelectStudent(StudentWith(50, active: false));
        basket.SetLine(1, BasketLine.For(1, ItemWith("Toast", 10)));

        basket.Validate().Problem.Should().Be(BasketProblem.StudentInactive);
    }

    [Fact]
    public void Execute_is_blocked_when_the_purchase_breaks_the_floor()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SelectStudent(StudentWith(50));
        basket.SetLine(1, BasketLine.Custom(1, "Stor beställning", Money.FromKronor(72.50m)));

        var validation = basket.Validate();

        validation.CanExecute.Should().BeFalse();
        validation.Problem.Should().Be(BasketProblem.NotEnoughMoney);
        validation.Message.Should().Contain("12,50 kr");
        validation.Check!.Shortfall.Should().Be(Money.FromKronor(12.50m));
    }

    [Fact]
    public void A_valid_purchase_can_execute_and_reports_the_new_balance()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SelectStudent(StudentWith(50));
        basket.SetLine(1, BasketLine.For(1, ItemWith("Toast", 10, id: 1)));
        basket.SetLine(2, BasketLine.For(2, ItemWith("Juice", 15, id: 2), quantity: 2));

        var validation = basket.Validate();

        validation.CanExecute.Should().BeTrue();
        validation.Total.Should().Be(Money.FromKronor(40));
        validation.Check!.BalanceAfter.Should().Be(Money.FromKronor(10));
    }

    [Fact]
    public void A_zero_quantity_blocks_execute()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SelectStudent(StudentWith(50));
        basket.SetLine(1, BasketLine.For(1, ItemWith("Toast", 10)) with { Quantity = 0 });

        basket.Validate().Problem.Should().Be(BasketProblem.InvalidQuantity);
    }

    [Fact]
    public void Clearing_resets_the_student_and_the_rows()
    {
        var basket = new PurchaseBasket(Settings);
        basket.SelectStudent(StudentWith(50));
        basket.SetLine(1, BasketLine.For(1, ItemWith("Toast", 10)));

        basket.Clear();

        basket.Student.Should().BeNull();
        basket.Lines.Should().HaveCount(1);
        basket.Total.Should().Be(Money.Zero);
    }
}

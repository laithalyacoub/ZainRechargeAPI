using ZainRecharge.Application.Exceptions;
using ZainRecharge.Application.Recharges;

namespace ZainRecharge.Tests;

public class RechargeRequestValidatorTests
{
    [Theory]
    [InlineData("0791234567", "0791234567")]
    [InlineData("00962791234567", "0791234567")]   
    [InlineData("+962791234567", "0791234567")]
    [InlineData("079 123 4567", "0791234567")]
    [InlineData("079-123-4567", "0791234567")]
    [InlineData("00962 78 1234567", "0781234567")]
    [InlineData("0771234567", "0771234567")]
    public void Valid_numbers_are_cleaned(string input, string expected)
    {
        Assert.True(RechargeRequestValidator.TryNormalizeMobile(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("0701234567")]          // 70 is not 77, 78 or 79
    [InlineData("079123456")]           // too short
    [InlineData("07912345678")]         // too long
    [InlineData("07912ab567")]          // letters
    [InlineData("009620791234567")]     // 00962 followed by a leading 0
    [InlineData("\u0660\u0667\u0669\u0661\u0662\u0663\u0664\u0665\u0666\u0667")] // Arabic-Indic digits
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_numbers_are_rejected(string? input)
    {
        Assert.False(RechargeRequestValidator.TryNormalizeMobile(input, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(1000)]
    [InlineData(0.5)]
    public void Valid_amounts_are_accepted(double amount)
    {
        var valid = RechargeRequestValidator.ValidateAndNormalize(
            new CreateRechargeRequest("0791234567", (decimal)amount));

        Assert.Equal((decimal)amount, valid.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000.01)]
    [InlineData(5.123)]
    public void Invalid_amounts_are_rejected(double amount)
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            RechargeRequestValidator.ValidateAndNormalize(
                new CreateRechargeRequest("0791234567", (decimal)amount)));

        Assert.True(ex.Errors.ContainsKey("amount"));
    }

    [Fact]
    public void A_missing_amount_and_a_bad_number_are_both_reported_at_once()
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            RechargeRequestValidator.ValidateAndNormalize(new CreateRechargeRequest("123", null)));

        Assert.True(ex.Errors.ContainsKey("mobileNumber"));
        Assert.True(ex.Errors.ContainsKey("amount"));
    }
}

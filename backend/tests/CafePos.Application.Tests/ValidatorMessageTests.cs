using CafePos.Application.Features.Locations;
using CafePos.Application.Features.Users;
using CafePos.Application.Features.VariationOverrides;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CafePos.Application.Tests;

/// <summary>The validators keep the field keys and wording the API produced with DataAnnotations (clients display them).</summary>
public class ValidatorMessageTests
{
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddApplication().BuildServiceProvider();

    private static Dictionary<string, string[]> Validate<T>(T request)
    {
        var validator = (IValidator<T>)Services.GetService(typeof(IValidator<T>))!;
        return validator.Validate(request).Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
    }

    [Fact]
    public void An_empty_user_request_reports_every_required_field_with_the_original_wording()
    {
        var errors = Validate(new CreateUserRequest());

        Assert.Equal(["The Email field is required.", "The Email field is not a valid e-mail address."], errors["Email"]);
        Assert.Equal(["The DisplayName field is required."], errors["DisplayName"]);
        Assert.Equal(["The Password field is required.", "The field Password must be a string or array type with a minimum length of '8'."], errors["Password"]);
    }

    [Fact]
    public void Nested_assignments_are_reported_with_an_indexed_key()
    {
        var errors = Validate(new CreateUserRequest
        {
            Email = "a@b.c", DisplayName = "A", Password = "Password#12345",
            Assignments = [new AssignmentRequest { Role = "" }]
        });

        Assert.Equal(["The Role field is required."], errors["Assignments[0].Role"]);
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("123456", true)]
    [InlineData("12", false)]
    [InlineData("1234567", false)]
    [InlineData("12a4", false)]
    public void A_pin_is_four_to_six_digits(string pin, bool valid)
    {
        var errors = Validate(new SetPinRequest { Pin = pin });

        Assert.Equal(valid, errors.Count == 0);
        if (!valid) Assert.Equal("PIN must be 4 to 6 digits.", errors["Pin"][0]);
    }

    [Fact]
    public void Range_and_length_messages_match_the_original_text()
    {
        var errors = Validate(new CreateLocationRequest { Name = new string('x', 201), TimeZone = "Asia/Manila", VatRatePercent = 101m });

        Assert.Equal(["The field Name must be a string or array type with a maximum length of '200'."], errors["Name"]);
        Assert.Equal(["The field VatRatePercent must be between 0 and 100."], errors["VatRatePercent"]);
    }

    [Fact]
    public void An_email_needs_a_single_at_sign_that_is_neither_first_nor_last()
    {
        Assert.Empty(Validate(new AssignStaffByEmailRequest { Email = "a@b", Role = "Cashier" }));
        Assert.Contains("Email", Validate(new AssignStaffByEmailRequest { Email = "@b", Role = "Cashier" }).Keys);
        Assert.Contains("Email", Validate(new AssignStaffByEmailRequest { Email = "a@", Role = "Cashier" }).Keys);
        Assert.Contains("Email", Validate(new AssignStaffByEmailRequest { Email = "a@b@c", Role = "Cashier" }).Keys);
    }

    [Fact]
    public void Nullable_numbers_are_only_checked_when_present()
    {
        Assert.Empty(Validate(new UpsertLocationOverrideRequest()));
        Assert.Contains("PriceOverride", Validate(new UpsertLocationOverrideRequest { PriceOverride = -5m }).Keys);
    }
}

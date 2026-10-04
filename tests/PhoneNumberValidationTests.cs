using QueueManagement.Shared;

namespace QueueManagement.Tests;

public class PhoneNumberValidationTests
{
    [Theory]
    [InlineData("0712345678", true)]
    [InlineData("1234567890", true)]
    [InlineData("071 234 5678", false)]
    [InlineData("071234567", false)]
    [InlineData("07123456789", false)]
    [InlineData("abcdefghij", false)]
    [InlineData("", false)]
    public void IsValid_RequiresExactlyTenDigits(string phoneNumber, bool expected)
    {
        Assert.Equal(expected, PhoneNumberValidation.IsValid(phoneNumber));
    }
}
namespace QueueManagement.Shared;

public static class PhoneNumberValidation
{
    public static bool IsValid(string? phoneNumber) =>
        phoneNumber is { Length: 10 } && phoneNumber.All(char.IsAsciiDigit);
}
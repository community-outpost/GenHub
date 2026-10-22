using System;
using System.ComponentModel.DataAnnotations;

namespace GenHub.Common.Validation;

/// <summary>
/// Maximum-length attribute with a localized error message.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LocalizedMaxLengthAttribute : MaxLengthAttribute
{
    private readonly string _resourceKey;
    private readonly string _fallbackMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizedMaxLengthAttribute"/> class.
    /// </summary>
    /// <param name="length">The maximum length.</param>
    /// <param name="resourceKey">The resource key for the localized message.</param>
    /// <param name="fallbackMessage">The English fallback message.</param>
    public LocalizedMaxLengthAttribute(int length, string resourceKey, string fallbackMessage)
        : base(length)
    {
        _resourceKey = resourceKey;
        _fallbackMessage = fallbackMessage;
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        return ValidationResourceResolver.FormatMessage(_resourceKey, _fallbackMessage, name, Length);
    }
}

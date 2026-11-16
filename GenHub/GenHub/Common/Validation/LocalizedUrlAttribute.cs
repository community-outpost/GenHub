using System;
using System.ComponentModel.DataAnnotations;

namespace GenHub.Common.Validation;

/// <summary>
/// URL attribute with a localized error message.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LocalizedUrlAttribute : ValidationAttribute
{
    private static readonly UrlAttribute InnerValidator = new();
    private readonly string _resourceKey;
    private readonly string _fallbackMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizedUrlAttribute"/> class.
    /// </summary>
    /// <param name="resourceKey">The resource key for the localized message.</param>
    /// <param name="fallbackMessage">The English fallback message.</param>
    public LocalizedUrlAttribute(string resourceKey, string fallbackMessage)
    {
        _resourceKey = resourceKey;
        _fallbackMessage = fallbackMessage;
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return InnerValidator.IsValid(value);
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        return ValidationResourceResolver.FormatMessage(_resourceKey, _fallbackMessage, name);
    }
}

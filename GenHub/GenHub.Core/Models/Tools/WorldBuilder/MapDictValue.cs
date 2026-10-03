using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A single typed value inside a map dictionary.
/// </summary>
/// <param name="Key">Dictionary key name.</param>
/// <param name="Type">Value type tag.</param>
/// <param name="IntValue">Integer payload for bool/int/color values.</param>
/// <param name="RealValue">Float payload for real values.</param>
/// <param name="StringValue">String payload for ascii/unicode values.</param>
public sealed record MapDictValue(
    string Key,
    WorldBuilderConstants.DictValueType Type,
    int IntValue = 0,
    float RealValue = 0f,
    string StringValue = "")
{
    /// <summary>Gets a value indicating whether the value is set.</summary>
    public bool AsBool => IntValue != 0;

    /// <summary>Gets the string payload, falling back to the integer for numeric types.</summary>
    public string AsDisplayString
    {
        get
        {
            if (Type is WorldBuilderConstants.DictValueType.AsciiString or WorldBuilderConstants.DictValueType.UnicodeString)
            {
                return StringValue;
            }

            if (Type == WorldBuilderConstants.DictValueType.Real)
            {
                return RealValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

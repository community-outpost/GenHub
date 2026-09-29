namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// A single live validation issue shown in the editor issues panel.
/// </summary>
/// <param name="Message">The human readable issue text.</param>
/// <param name="IsError">True for errors, false for warnings.</param>
public sealed record IniValidationRow(string Message, bool IsError);

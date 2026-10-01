using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Results;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Resolves installation-step argument bindings against delivered package files.
/// Fail-closed: any unresolvable binding fails the step with the step, file, and key
/// named, so a publisher packaging change surfaces as a diagnosable install error
/// rather than an installer run with a wrong or blank argument.
/// </summary>
public static class InstallationArgumentBindingResolver
{
    /// <summary>
    /// Resolves a step's effective arguments, filling bound entries from package files.
    /// </summary>
    /// <param name="step">The installation step.</param>
    /// <param name="workingDirectory">The delivered content directory bindings read from.</param>
    /// <returns>The effective arguments, or a failure naming the offending binding.</returns>
    public static OperationResult<List<string>> ResolveArguments(InstallationStep step, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.ArgumentBindings is not { Count: > 0 })
        {
            return OperationResult<List<string>>.CreateSuccess(step.Arguments ?? []);
        }

        if (!string.IsNullOrWhiteSpace(step.StepKey))
        {
            return OperationResult<List<string>>.CreateFailure(
                $"Installation step '{step.Name}' declares both argument bindings and an explicit step key. Omit the step key so it derives from the resolved arguments.");
        }

        var resolved = step.Arguments is null ? new List<string>() : new List<string>(step.Arguments);
        var seenIndices = new HashSet<int>();
        foreach (var binding in step.ArgumentBindings)
        {
            if (binding is null)
            {
                return OperationResult<List<string>>.CreateFailure($"Installation step '{step.Name}' declares a null argument binding.");
            }

            if (binding.ArgumentIndex < 0 || binding.ArgumentIndex >= resolved.Count)
            {
                return OperationResult<List<string>>.CreateFailure(
                    $"Installation step '{step.Name}' binds argument #{binding.ArgumentIndex} but declares {resolved.Count} argument(s).");
            }

            if (!seenIndices.Add(binding.ArgumentIndex))
            {
                return OperationResult<List<string>>.CreateFailure(
                    $"Installation step '{step.Name}' declares duplicate bindings for argument #{binding.ArgumentIndex}.");
            }
        }

        foreach (var binding in step.ArgumentBindings)
        {
            var valueResult = ResolveBinding(step, binding, resolved.Count, workingDirectory);
            if (!valueResult.Success || valueResult.Data is null)
            {
                return OperationResult<List<string>>.CreateFailure(valueResult.Errors);
            }

            resolved[binding.ArgumentIndex] = valueResult.Data;
        }

        return OperationResult<List<string>>.CreateSuccess(resolved);
    }

    private static OperationResult<string> ResolveBinding(
        InstallationStep step,
        InstallationArgumentBinding binding,
        int argumentCount,
        string workingDirectory)
    {
        if (binding.ArgumentIndex < 0 || binding.ArgumentIndex >= argumentCount)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' binds argument #{binding.ArgumentIndex} but declares {argumentCount} argument(s).");
        }

        if (!string.Equals(binding.Source, ManifestConstants.InstallationBindingJsonSource, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' uses unsupported binding source '{binding.Source}'. Supported sources: {ManifestConstants.InstallationBindingJsonSource}.");
        }

        var fileResult = ResolveBindingFile(step, binding, workingDirectory);
        if (!fileResult.Success || fileResult.Data is null)
        {
            return OperationResult<string>.CreateFailure(fileResult.Errors);
        }

        return ReadJsonProperty(step, binding, fileResult.Data);
    }

    private static OperationResult<string> ResolveBindingFile(
        InstallationStep step,
        InstallationArgumentBinding binding,
        string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(binding.RelativePath))
        {
            return OperationResult<string>.CreateFailure($"Installation step '{step.Name}' declares a binding with no file path.");
        }

        string fullPath = string.Empty;
        try
        {
            var relative = binding.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            fullPath = Path.GetFullPath(Path.Combine(workingDirectory, relative));
        }
        catch (IOException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' declares an unusable binding path '{binding.RelativePath}'.");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' declares an unusable binding path '{binding.RelativePath}'.");
        }
        catch (ArgumentException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' declares an unusable binding path '{binding.RelativePath}'.");
        }
        catch (NotSupportedException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' declares an unusable binding path '{binding.RelativePath}'.");
        }

        if (!PathHelper.IsPathWithinDirectory(workingDirectory, fullPath))
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' binding path '{binding.RelativePath}' escapes the content directory.");
        }

        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                return OperationResult<string>.CreateFailure(
                    $"Installation step '{step.Name}' binding file '{binding.RelativePath}' was not delivered.");
            }

            if (info.Length > ManifestConstants.InstallationBindingMaxFileSizeBytes)
            {
                return OperationResult<string>.CreateFailure(
                    $"Installation step '{step.Name}' binding file '{binding.RelativePath}' exceeds the {ManifestConstants.InstallationBindingMaxFileSizeBytes} byte limit.");
            }
        }
        catch (IOException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' cannot access binding file '{binding.RelativePath}'.");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' cannot access binding file '{binding.RelativePath}'.");
        }
        catch (ArgumentException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' cannot access binding file '{binding.RelativePath}'.");
        }
        catch (NotSupportedException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' cannot access binding file '{binding.RelativePath}'.");
        }

        return OperationResult<string>.CreateSuccess(fullPath);
    }

    private static OperationResult<string> ReadJsonProperty(
        InstallationStep step,
        InstallationArgumentBinding binding,
        string fullPath)
    {
        if (string.IsNullOrWhiteSpace(binding.Key))
        {
            return OperationResult<string>.CreateFailure($"Installation step '{step.Name}' declares a binding with no key.");
        }

        try
        {
            using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fileStream.Length > ManifestConstants.InstallationBindingMaxFileSizeBytes)
            {
                return OperationResult<string>.CreateFailure(
                    $"Installation step '{step.Name}' binding file '{binding.RelativePath}' exceeds the {ManifestConstants.InstallationBindingMaxFileSizeBytes} byte limit.");
            }

            var buffer = new byte[ManifestConstants.InstallationBindingMaxFileSizeBytes + 1];
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = fileStream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead > ManifestConstants.InstallationBindingMaxFileSizeBytes)
            {
                return OperationResult<string>.CreateFailure(
                    $"Installation step '{step.Name}' binding file '{binding.RelativePath}' exceeds the {ManifestConstants.InstallationBindingMaxFileSizeBytes} byte limit.");
            }

            using var document = JsonDocument.Parse(buffer.AsMemory(0, totalRead));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OperationResult<string>.CreateFailure(
                    $"Installation step '{step.Name}' binding file '{binding.RelativePath}' is not a JSON object.");
            }

            var propertyValue = FindCaseInsensitiveStringProperty(document.RootElement, binding.Key);
            if (propertyValue != null)
            {
                return OperationResult<string>.CreateSuccess(propertyValue);
            }

            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' binding key '{binding.Key}' has no usable string value in '{binding.RelativePath}'.");
        }
        catch (IOException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' cannot read binding file '{binding.RelativePath}'.");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' cannot read binding file '{binding.RelativePath}'.");
        }
        catch (JsonException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' cannot read binding file '{binding.RelativePath}'.");
        }
        catch (ArgumentException)
        {
            return OperationResult<string>.CreateFailure(
                $"Installation step '{step.Name}' cannot read binding file '{binding.RelativePath}'.");
        }
    }

    private static string? FindCaseInsensitiveStringProperty(JsonElement root, string key)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }

                break;
            }
        }

        return null;
    }
}

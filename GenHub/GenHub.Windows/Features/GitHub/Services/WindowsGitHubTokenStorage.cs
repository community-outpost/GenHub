using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Features.Workspace;

namespace GenHub.Windows.Features.GitHub.Services;

/// <summary>
/// Windows-specific GitHub token storage using DPAPI.
/// </summary>
public class WindowsGitHubTokenStorage : IGitHubTokenStorage
{
    private readonly string _tokenFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsGitHubTokenStorage"/> class.
    /// </summary>
    public WindowsGitHubTokenStorage()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var genHubDir = Path.Combine(appData, AppConstants.AppName);
        Directory.CreateDirectory(genHubDir);
        _tokenFilePath = Path.Combine(genHubDir, AppConstants.TokenFileName);
    }

    /// <summary>
    /// Saves the GitHub token securely using DPAPI.
    /// </summary>
    /// <param name="token">The secure string token to save.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SaveTokenAsync(SecureString token)
    {
        if (token == null || token.Length == 0)
        {
            throw new ArgumentException("Token cannot be null or empty", nameof(token));
        }

        // Convert SecureString to plain text temporarily
        var plainText = SecureStringToString(token);

        try
        {
            // Encrypt using DPAPI
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            // Save to file
            await File.WriteAllBytesAsync(_tokenFilePath, encryptedBytes);
        }
        finally
        {
            // Clear sensitive data from memory
            plainText = null;
        }
    }

    /// <summary>
    /// Deletes the stored GitHub token.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task DeleteTokenAsync()
    {
        FileOperationsService.DeleteFileIfExists(_tokenFilePath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if a GitHub token is stored.
    /// </summary>
    /// <returns>True if a token is stored, otherwise false.</returns>
    public bool HasToken()
    {
        return File.Exists(_tokenFilePath);
    }

    /// <summary>
    /// Loads the stored GitHub token.
    /// </summary>
    /// <returns>The secure string token if available, otherwise null.</returns>
    public async Task<SecureString?> LoadTokenAsync()
    {
        if (!File.Exists(_tokenFilePath))
        {
            return null;
        }

        try
        {
            // Read encrypted bytes
            var encryptedBytes = await File.ReadAllBytesAsync(_tokenFilePath);

            // Decrypt using DPAPI
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var plainText = Encoding.UTF8.GetString(plainBytes);

            // Convert to SecureString
            var secureString = StringToSecureString(plainText);

            // Clear plain text
            Array.Clear(plainBytes, 0, plainBytes.Length);

            return secureString;
        }
        catch (CryptographicException)
        {
            // Token was encrypted by different user or machine - delete it
            await DeleteTokenAsync();
            return null;
        }
    }

    private static string SecureStringToString(SecureString secureString)
    {
        var ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
        try
        {
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    private static SecureString StringToSecureString(string input)
    {
        var secureString = new SecureString();
        foreach (var c in input)
        {
            secureString.AppendChar(c);
        }

        secureString.MakeReadOnly();
        return secureString;
    }
}

using GenHub.Core.Models.Providers;
using GenHub.Features.Tools.ViewModels.Dialogs;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for variant gating in <see cref="AddArtifactDialogViewModel"/>.
/// Bundle-mode releases install every artifact together, so the dialog must
/// neither expose nor persist variant data when variants are disallowed.
/// </summary>
public sealed class AddArtifactDialogVariantTests
{
    /// <summary>
    /// Variants stay enabled by default so existing callers keep current behavior.
    /// </summary>
    [Fact]
    public void AllowVariants_DefaultsToTrue()
    {
        using var vm = new AddArtifactDialogViewModel(_ => { });

        Assert.True(vm.AllowVariants);
    }

    /// <summary>
    /// A bundle-mode artifact must drop variant fields on save even when they
    /// were set programmatically.
    /// </summary>
    [Fact]
    public void CreateArtifact_BundleMode_StripsVariantFields()
    {
        ReleaseArtifact? created = null;
        using var vm = new AddArtifactDialogViewModel(a => created = a, null, allowVariants: false);
        vm.UseLocalFile = false;
        vm.DownloadUrl = "https://example.com/mod.zip";
        vm.Filename = "mod.zip";
        vm.VariantAxisSelector.SetValue("resolution");
        vm.Variant = "1080p";
        vm.IsDefaultVariant = true;

        vm.CreateArtifactCommand.Execute(null);

        Assert.NotNull(created);
        Assert.Null(created.VariantAxis);
        Assert.Null(created.Variant);
        Assert.False(created.IsDefaultVariant);
    }

    /// <summary>
    /// A variants-mode artifact must preserve its variant fields on save.
    /// </summary>
    [Fact]
    public void CreateArtifact_VariantsMode_PreservesVariantFields()
    {
        ReleaseArtifact? created = null;
        using var vm = new AddArtifactDialogViewModel(a => created = a);
        vm.UseLocalFile = false;
        vm.DownloadUrl = "https://example.com/mod-1080p.zip";
        vm.Filename = "mod-1080p.zip";
        vm.VariantAxisSelector.SetValue("resolution");
        vm.Variant = "1080p";
        vm.IsDefaultVariant = true;

        vm.CreateArtifactCommand.Execute(null);

        Assert.NotNull(created);
        Assert.Equal("resolution", created.VariantAxis);
        Assert.Equal("1080p", created.Variant);
        Assert.True(created.IsDefaultVariant);
    }

    /// <summary>
    /// Editing a legacy variant-carrying artifact in bundle mode must strip
    /// its variant fields on save.
    /// </summary>
    [Fact]
    public void EditArtifact_BundleMode_StripsLegacyVariantFields()
    {
        var existing = new ReleaseArtifact
        {
            Filename = "mod.zip",
            DownloadUrl = "https://example.com/mod.zip",
            Size = 10,
            Sha256 = "abc",
            VariantAxis = "resolution",
            Variant = "1080p",
            IsDefaultVariant = true,
        };
        ReleaseArtifact? saved = null;
        using var vm = new AddArtifactDialogViewModel(existing, a => saved = a, null, allowVariants: false);

        Assert.False(vm.AllowVariants);

        vm.CreateArtifactCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Null(saved.VariantAxis);
        Assert.Null(saved.Variant);
        Assert.False(saved.IsDefaultVariant);
    }
}

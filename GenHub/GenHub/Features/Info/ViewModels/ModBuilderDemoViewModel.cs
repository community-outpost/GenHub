using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Infrastructure.Converters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the interactive ModBuilder demo with a placeholder sample project.
/// </summary>
public partial class ModBuilderDemoViewModel : ObservableObject
{
    private readonly INotificationService? _notificationService;
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty]
    private string _projectName = "ShockwaveDemo.mbproj";

    [ObservableProperty]
    private ModBuilderDemoFile? _selectedAvailableFile;

    [ObservableProperty]
    private ModBuilderDemoPack? _selectedPack;

    [ObservableProperty]
    private ModBuilderDemoFile? _selectedPackItem;

    [ObservableProperty]
    private string _selectedTextureVariant = "1080p";

    [ObservableProperty]
    private string _selectedAudioVariant = "English";

    [ObservableProperty]
    private string _manifestPreview = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModBuilderDemoViewModel"/> class.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="localizationService">Optional localization service for demo strings.</param>
    public ModBuilderDemoViewModel(
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
    {
        _notificationService = notificationService;
        _localizationService = localizationService;
        AvailableFiles = new ObservableCollection<ModBuilderDemoFile>();
        BundlePacks = new ObservableCollection<ModBuilderDemoPack>();
        BuildLog = new ObservableCollection<string>();
        TextureVariants = new List<string> { "1080p", "4K" };
        AudioVariants = new List<string> { "English", "German" };
        SeedPlaceholders();
        SelectedPack = BundlePacks.FirstOrDefault();
        StatusMessage = DemoText("Info.Demo.Tools.ModBuilder.Status.Ready", "Assemble bundle packs, then build the sample project.");
        RefreshManifestPreview();
    }

    /// <summary>
    /// Gets or sets an action invoked when the demo requests navigation to an info section.
    /// </summary>
    public Action<string>? NavigationRequested { get; set; }

    /// <summary>
    /// Gets the placeholder project files available for bundling.
    /// </summary>
    public ObservableCollection<ModBuilderDemoFile> AvailableFiles { get; }

    /// <summary>
    /// Gets the placeholder bundle packs.
    /// </summary>
    public ObservableCollection<ModBuilderDemoPack> BundlePacks { get; }

    /// <summary>
    /// Gets the simulated build log lines.
    /// </summary>
    public ObservableCollection<string> BuildLog { get; }

    /// <summary>
    /// Gets the texture variant options.
    /// </summary>
    public IReadOnlyList<string> TextureVariants { get; }

    /// <summary>
    /// Gets the audio variant options.
    /// </summary>
    public IReadOnlyList<string> AudioVariants { get; }

    /// <summary>
    /// Gets the total number of bundled files across all packs.
    /// </summary>
    public int TotalBundledFiles => BundlePacks.Sum(p => p.Items.Count);

    /// <summary>
    /// Adds the selected available file to the selected bundle pack.
    /// </summary>
    [RelayCommand]
    private void AddFileToPack()
    {
        if (SelectedAvailableFile == null || SelectedPack == null)
        {
            return;
        }

        if (SelectedPack.Items.Any(i => string.Equals(i.Name, SelectedAvailableFile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            _notificationService?.ShowWarning(
                DemoText("Info.Demo.Tools.ModBuilder.Toast.Title", "Demo"),
                DemoText("Info.Demo.Tools.ModBuilder.Toast.DuplicateMessage", "That file is already in this pack."),
                NotificationDurations.Short);
            return;
        }

        SelectedPack.Items.Add(new ModBuilderDemoFile(
            SelectedAvailableFile.Name,
            SelectedAvailableFile.TargetPath,
            SelectedAvailableFile.Conversion,
            SelectedAvailableFile.SizeKb));
        RefreshManifestPreview();
    }

    /// <summary>
    /// Removes the selected item from the selected bundle pack.
    /// </summary>
    [RelayCommand]
    private void RemovePackItem()
    {
        if (SelectedPack == null || SelectedPackItem == null)
        {
            return;
        }

        SelectedPack.Items.Remove(SelectedPackItem);
        SelectedPackItem = null;
        RefreshManifestPreview();
    }

    /// <summary>
    /// Moves the selected pack item up in the bundle order.
    /// </summary>
    [RelayCommand]
    private void MovePackItemUp()
    {
        MoveSelectedPackItem(-1);
    }

    /// <summary>
    /// Moves the selected pack item down in the bundle order.
    /// </summary>
    [RelayCommand]
    private void MovePackItemDown()
    {
        MoveSelectedPackItem(1);
    }

    /// <summary>
    /// Converts all TGA textures in the selected pack to DDS.
    /// </summary>
    [RelayCommand]
    private void ConvertAllToDds()
    {
        if (SelectedPack == null)
        {
            return;
        }

        var converted = 0;
        foreach (var item in SelectedPack.Items)
        {
            if (item.Name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Conversion, "TGA->DDS", StringComparison.Ordinal))
            {
                item.Conversion = "TGA->DDS";
                item.TargetPath = Path.ChangeExtension(item.TargetPath, ".dds");
                converted++;
            }
        }

        StatusMessage = converted > 0
            ? DemoText("Info.Demo.Tools.ModBuilder.Status.Converted", "Converted pack textures to DDS.")
            : DemoText("Info.Demo.Tools.ModBuilder.Status.NothingToConvert", "No unconverted TGA textures in this pack.");
        RefreshManifestPreview();
    }

    /// <summary>
    /// Simulates building the selected bundle pack.
    /// </summary>
    [RelayCommand]
    private void BuildPack()
    {
        if (SelectedPack == null)
        {
            return;
        }

        BuildLog.Clear();
        BuildLog.Add($"Resolving {SelectedPack.Items.Count} file(s) for pack '{SelectedPack.Name}'...");
        foreach (var item in SelectedPack.Items)
        {
            BuildLog.Add(item.Conversion == "None" ? $"Copy {item.Name} -> {item.TargetPath}" : $"{item.Conversion} {item.Name} -> {item.TargetPath}");
        }

        BuildLog.Add($"Writing {SelectedPack.Name}.big ({SelectedPack.TotalSizeKb} KB, {SelectedTextureVariant} textures, {SelectedAudioVariant} audio)...");
        BuildLog.Add("Build complete: 0 errors. (Simulated)");
        _notificationService?.ShowSuccess(
            DemoText("Info.Demo.Tools.ModBuilder.Toast.Title", "Demo"),
            DemoText("Info.Demo.Tools.ModBuilder.Toast.BuildMessage", "Bundle pack built. (Simulated)"),
            NotificationDurations.Short);
    }

    /// <summary>
    /// Navigates to the Content Manifests guide.
    /// </summary>
    [RelayCommand]
    private void LearnAboutManifests()
    {
        NavigationRequested?.Invoke(InfoConstants.SectionContentManifests);
    }

    partial void OnSelectedTextureVariantChanged(string value)
    {
        RefreshManifestPreview();
    }

    partial void OnSelectedAudioVariantChanged(string value)
    {
        RefreshManifestPreview();
    }

    private void MoveSelectedPackItem(int direction)
    {
        if (SelectedPack == null || SelectedPackItem == null)
        {
            return;
        }

        var index = SelectedPack.Items.IndexOf(SelectedPackItem);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= SelectedPack.Items.Count)
        {
            return;
        }

        SelectedPack.Items.Move(index, target);
        RefreshManifestPreview();
    }

    private void SeedPlaceholders()
    {
        AvailableFiles.Add(new ModBuilderDemoFile("GameData.ini", "Data/INI/GameData.ini", "None", 148));
        AvailableFiles.Add(new ModBuilderDemoFile("tank_01.tga", "Art/Textures/tank_01.tga", "None", 1024));
        AvailableFiles.Add(new ModBuilderDemoFile("tank_01_4k.tga", "Art/Textures/tank_01_4k.tga", "None", 4096));
        AvailableFiles.Add(new ModBuilderDemoFile("unit_ack_en.wav", "Audio/Speech/English/unit_ack.wav", "None", 96));
        AvailableFiles.Add(new ModBuilderDemoFile("unit_ack_de.wav", "Audio/Speech/German/unit_ack.wav", "None", 104));
        AvailableFiles.Add(new ModBuilderDemoFile("game.str", "Data/Csf/game.str", "CSF Compile", 64));

        var core = new ModBuilderDemoPack("Core Files", "Required INI and string tables.");
        core.Items.Add(new ModBuilderDemoFile("GameData.ini", "Data/INI/GameData.ini", "None", 148));
        core.Items.Add(new ModBuilderDemoFile("game.str", "Data/Csf/game.str", "CSF Compile", 64));
        BundlePacks.Add(core);

        var textures = new ModBuilderDemoPack("HD Textures", "Variant: 1080p or 4K.");
        textures.Items.Add(new ModBuilderDemoFile("tank_01.tga", "Art/Textures/tank_01.tga", "None", 1024));
        BundlePacks.Add(textures);

        var audio = new ModBuilderDemoPack("Localized Audio", "Variant: English or German.");
        audio.Items.Add(new ModBuilderDemoFile("unit_ack_en.wav", "Audio/Speech/English/unit_ack.wav", "None", 96));
        BundlePacks.Add(audio);
    }

    private void RefreshManifestPreview()
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("  \"manifestId\": \"1.0.demo.mod.shockwavedemo\",");
        builder.AppendLine("  \"name\": \"Shockwave Demo\",");
        builder.AppendLine("  \"version\": \"1.0.0\",");
        builder.AppendLine("  \"contentType\": \"Mod\",");
        builder.AppendLine($"  \"textureVariant\": \"{SelectedTextureVariant}\",");
        builder.AppendLine($"  \"audioVariant\": \"{SelectedAudioVariant}\",");
        builder.AppendLine("  \"bundles\": [");

        var packs = BundlePacks.ToList();
        for (var i = 0; i < packs.Count; i++)
        {
            builder.AppendLine("    {");
            builder.AppendLine($"      \"name\": \"{packs[i].Name}\",");
            builder.AppendLine($"      \"files\": {packs[i].Items.Count},");
            builder.AppendLine($"      \"sizeKb\": {packs[i].TotalSizeKb}");
            builder.Append("    }");
            builder.AppendLine(i < packs.Count - 1 ? "," : string.Empty);
        }

        builder.AppendLine("  ]");
        builder.AppendLine("}");
        ManifestPreview = builder.ToString();
        OnPropertyChanged(nameof(TotalBundledFiles));
    }

    private string DemoText(string key, string fallback) =>
        LocalizationConverterHelper.GetLocalizedOrDefault(_localizationService, key, fallback);
}

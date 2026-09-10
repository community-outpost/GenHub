// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "StyleCop.CSharp.DocumentationRules",
    "SA1633:File should have header",
    Justification = "Licensing and other information is provided in seperate files.")]

[assembly: SuppressMessage(
    "Design",
    "CS-R1138:Inappropriate ordering of parameters",
    Justification = "Parameters ordered to match C# idiomatic patterns where primary outputs follow inputs.")]

[assembly: SuppressMessage(
    "Design",
    "CS-R1138:Inappropriate ordering of parameters",
    Scope = "type",
    Target = "~T:GenHub.Core.Models.Enums.ManifestResolvers.ResolvedManifestType",
    Justification = "Enum values are ordered logically by priority, not alphabetically.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.DocumentationRules",
    "SA1649:FileNameMustMatchTypeName",
    Justification = "Common exceptions are grouped in a single file for better maintainability.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.MaintainabilityRules",
    "SA1402:FileMayOnlyContainASingleType",
    Scope = "type",
    Target = "~T:GenHub.Core.Models.Tools.ModBuilder.Converters.BundlePackListConverter",
    Justification = "BundleConverterModels.cs groups related converter types.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.DocumentationRules",
    "SA1633:File should have header",
    Justification = "Licensing and other information is provided in seperate files.")]

[assembly: SuppressMessage(
    "Design",
    "CS-R1138:Inappropriate ordering of parameters",
    Scope = "type",
    Target = "~T:GenHub.Core.Models.Tools.ModBuilder.Converters.BundlePackListConverter",
    Justification = "System.Text.Json requires ref Utf8JsonReader as the first parameter in JsonConverter<T>.Read overrides.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.DocumentationRules",
    "SA1649:FileNameMustMatchTypeName",
    Scope = "type",
    Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonConfigRoot",
    Justification = "PythonConfigModels.cs groups related DTO types.")]

[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonBundlesConfig", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonBundleItem", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonBundlePack", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonBundleFileGroup", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonSourceTargetPair", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonBundleEvent", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonModJsonFilesConfig", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonModJsonFilesBuild", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonModFoldersConfig", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.PythonModFoldersData", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.SimplifiedConfigRoot", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.SimplifiedBundleItem", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]
[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType", Scope = "type", Target = "~T:GenHub.Core.Models.Tools.ModBuilder.SimplifiedBundlePack", Justification = "Python configuration DTOs are grouped in PythonConfigModels.cs.")]

// -----------------------------------------------------------------------------
//  GlobalSuppressions.cs
//  This file contains code analysis suppression attributes for the entire project.
//  For more information on suppressing warnings, see the .NET documentation.
//
//  Please keep suppressions well-documented and justified.
//  When adding a new suppression, include a comment explaining the rationale.
//
//  See CONTRIBUTIONS.md for contribution guidelines.
//
//  Version: 2025-06-17
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "StyleCop.CSharp.SpacingRules",
    "SA1000:Keywords should be spaced correctly",
    Justification = "Conflicts with the C#9 introduction of the new() usage.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.SpacingRules",
    "SA1010:Opening square brackets should be spaced correctly",
    Justification = "Conflicts with shortend assignment of enumerations introduced in C#8.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.ReadabilityRules",
    "SA1101:Prefix local calls with this",
    Justification = "Microsoft guidelines do not require 'this.' prefix unless needed for clarity.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.OrderingRules",
    "SA1200:Using directives should be placed correctly",
    Justification = "Microsoft guidelines allow using directives inside or outside namespaces.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.OrderingRules",
    "SA1201:ElementsMustAppearInTheCorrectOrder",
    Justification = "Known StyleCop bug with .NET 8+ record declarations; does not affect code order.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.OrderingRules",
    "SA1208:System using directives should be placed before other using directives",
    Justification = "Using directives are sorted alphabetically, which coincides with Visual Studio's Sort & Remove")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.NamingRules",
    "SA1300:Element should begin with upper-case letter",
    Justification = "Microsoft guidelines allow underscores in certain cases, such as test methods.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.NamingRules",
    "SA1309:Field names should not begin with underscore",
    Justification = "Microsoft guidelines allow _camelCase for private fields.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.LayoutRules",
    "SA1503:Braces should not be omitted",
    Justification = "Community Outpost Code Guidelines allow braces to be omitted.")]

[assembly: SuppressMessage(
    "StyleCop.CSharp.DocumentationRules",
    "SA1633:File should have header",
    Justification = "Licensing and other information is provided in seperate files.")]
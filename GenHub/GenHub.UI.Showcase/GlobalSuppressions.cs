// <copyright file="GlobalSuppressions.cs" company="Community Outpost">
// Copyright (c) Community Outpost. All rights reserved.
// </copyright>

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "StyleCop.CSharp.ReadabilityRules",
    "SA1101:Prefix local calls with this",
    Justification = "Matches the other projects, which do not require 'this.' unless needed for clarity.")]

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A waypoint link between two waypoint ids.
/// </summary>
/// <param name="Waypoint1">First waypoint id.</param>
/// <param name="Waypoint2">Second waypoint id.</param>
public sealed record MapWaypointLink(int Waypoint1, int Waypoint2);

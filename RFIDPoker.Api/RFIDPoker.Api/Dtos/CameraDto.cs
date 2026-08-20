using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Dtos;

public record CameraDto(
    int Id,
    string Name,
    string ObsSceneName,
    CameraRole Role,
    int SortOrder,
    bool Enabled);

public record CreateCameraRequest(
    string Name,
    string ObsSceneName,
    CameraRole Role,
    int SortOrder,
    bool Enabled);

public record UpdateCameraRequest(
    string Name,
    string ObsSceneName,
    CameraRole Role,
    int SortOrder,
    bool Enabled);

/// <summary>Current camera-director status (for the admin UI status badge).</summary>
public record CameraStatusDto(
    bool Enabled,
    bool Connected,
    string? CurrentScene,
    string? DesiredScene,
    bool HandInProgress);

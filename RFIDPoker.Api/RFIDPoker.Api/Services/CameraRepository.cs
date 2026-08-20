using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Data;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface ICameraRepository
{
    Task<List<Camera>> GetAllAsync(CancellationToken ct = default);
    Task<Camera?> GetAsync(int id, CancellationToken ct = default);
    Task<Camera> AddAsync(Camera camera, CancellationToken ct = default);
    Task<bool> UpdateAsync(Camera camera, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Raised whenever the camera list changes so a running
    /// <see cref="CameraDirectorService"/> can refresh its snapshot without polling.
    /// </summary>
    event Action? Changed;
    void NotifyChanged();
}

public class CameraRepository(AppDbContext db) : ICameraRepository
{
    // Static event so the singleton director can subscribe even though the repository
    // itself is scoped (created per HTTP request via EF Core).
    private static event Action? StaticChanged;

    public event Action? Changed
    {
        add => StaticChanged += value;
        remove => StaticChanged -= value;
    }

    public void NotifyChanged() => StaticChanged?.Invoke();

    public Task<List<Camera>> GetAllAsync(CancellationToken ct = default)
        => db.Cameras.AsNoTracking()
            .OrderBy(c => c.Role)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);

    public Task<Camera?> GetAsync(int id, CancellationToken ct = default)
        => db.Cameras.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Camera> AddAsync(Camera camera, CancellationToken ct = default)
    {
        db.Cameras.Add(camera);
        await db.SaveChangesAsync(ct);
        NotifyChanged();
        return camera;
    }

    public async Task<bool> UpdateAsync(Camera camera, CancellationToken ct = default)
    {
        var existing = await db.Cameras.FirstOrDefaultAsync(c => c.Id == camera.Id, ct);
        if (existing is null) return false;
        existing.Name = camera.Name;
        existing.ObsSceneName = camera.ObsSceneName;
        existing.Role = camera.Role;
        existing.SortOrder = camera.SortOrder;
        existing.Enabled = camera.Enabled;
        await db.SaveChangesAsync(ct);
        NotifyChanged();
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var existing = await db.Cameras.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (existing is null) return false;
        db.Cameras.Remove(existing);
        await db.SaveChangesAsync(ct);
        NotifyChanged();
        return true;
    }
}

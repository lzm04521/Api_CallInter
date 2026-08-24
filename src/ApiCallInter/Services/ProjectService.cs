using System.Text.Json;
using ApiCallInter.Data;
using Microsoft.EntityFrameworkCore;

namespace ApiCallInter.Services;

public class ValidationException(string message) : Exception(message);

public class ProjectService(AppDbContext db)
{
    public Task<List<Project>> ListAsync() =>
        db.Projects.Include(p => p.Endpoints).OrderBy(p => p.Name).ToListAsync();

    public async Task<Project> CreateAsync(Project p)
    {
        ValidateProject(p);
        p.CreatedAt = p.UpdatedAt = DateTime.UtcNow;
        foreach (var e in p.Endpoints) { ValidateEndpoint(e); e.CreatedAt = e.UpdatedAt = DateTime.UtcNow; }
        db.Projects.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    public async Task<Project?> UpdateAsync(int id, Project input)
    {
        ValidateProject(input);
        var p = await db.Projects.FindAsync(id);
        if (p is null) return null;
        p.Name = input.Name; p.Description = input.Description;
        p.IntervalSeconds = input.IntervalSeconds; p.JitterMilliseconds = input.JitterMilliseconds;
        p.Enabled = input.Enabled; p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return p;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var p = await db.Projects.Include(x => x.Endpoints).FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return false;
        db.ApiEndpoints.RemoveRange(p.Endpoints);
        await db.RequestLogs.Where(l => l.ProjectId == id).ExecuteDeleteAsync();
        db.Projects.Remove(p);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<ApiEndpoint> CreateEndpointAsync(int projectId, ApiEndpoint e)
    {
        ValidateEndpoint(e);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId)) throw new ValidationException("项目不存在");
        e.ProjectId = projectId;
        e.CreatedAt = e.UpdatedAt = DateTime.UtcNow;
        db.ApiEndpoints.Add(e);
        await db.SaveChangesAsync();
        return e;
    }

    public async Task<ApiEndpoint?> UpdateEndpointAsync(int id, ApiEndpoint input)
    {
        ValidateEndpoint(input);
        var e = await db.ApiEndpoints.FindAsync(id);
        if (e is null) return null;
        e.Name = input.Name; e.Url = input.Url; e.Method = input.Method;
        e.Headers = input.Headers; e.Body = input.Body;
        e.TimeoutSeconds = input.TimeoutSeconds; e.Enabled = input.Enabled;
        e.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return e;
    }

    public async Task<bool> DeleteEndpointAsync(int id)
    {
        var e = await db.ApiEndpoints.FindAsync(id);
        if (e is null) return false;
        await db.RequestLogs.Where(l => l.EndpointId == id).ExecuteDeleteAsync();
        db.ApiEndpoints.Remove(e);
        await db.SaveChangesAsync();
        return true;
    }

    internal static void ValidateProject(Project p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) throw new ValidationException("项目名不能为空");
        if (p.IntervalSeconds < 30) throw new ValidationException("间隔不能小于 30 秒");
        if (p.JitterMilliseconds < 0) throw new ValidationException("抖动不能为负数");
        if (p.JitterMilliseconds >= p.IntervalSeconds * 1000) throw new ValidationException("抖动必须小于间隔毫秒数");
    }

    internal static void ValidateEndpoint(ApiEndpoint e)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new ValidationException("接口名不能为空");
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ValidationException("Url 必须是 http/https 绝对地址");
        if (e.Method is not ("GET" or "POST" or "PUT" or "HEAD")) throw new ValidationException("Method 仅支持 GET/POST/PUT/HEAD");
        if (e.TimeoutSeconds is < 1 or > 120) throw new ValidationException("超时必须在 1~120 秒");
        if (!string.IsNullOrWhiteSpace(e.Headers))
        {
            try { using var _ = JsonDocument.Parse(e.Headers); }
            catch (JsonException) { throw new ValidationException("Headers 必须是合法 JSON 对象"); }
        }
    }
}

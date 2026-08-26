namespace ApiCallInter.Data;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int IntervalSeconds { get; set; } = 300;
    public int JitterMilliseconds { get; set; } = 3000;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }   // 手动排序位（ReorderAsync 全量重编 1..n，新建项目取 max+1）
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ApiEndpoint> Endpoints { get; set; } = [];
}

public class ApiEndpoint
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Headers { get; set; } = "";
    public string Body { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class RequestLog
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int EndpointId { get; set; }
    public DateTime RequestedAt { get; set; }
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public int ElapsedMs { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

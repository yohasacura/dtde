using Dtde.Samples.MultiTenant.Data;
using Dtde.Samples.MultiTenant.Entities;
using Dtde.Samples.MultiTenant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.MultiTenant.Controllers;

/// <summary>
/// API controller for tenant-scoped task operations.
/// </summary>
[ApiController]
[Route("api/tenant/{tenantId}/tasks")]
public class TasksController : ControllerBase
{
    private readonly MultiTenantDbContext _context;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        MultiTenantDbContext context,
        ITenantContextAccessor tenantAccessor,
        ILogger<TasksController> logger)
    {
        _context = context;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Get tasks for a tenant (with filters).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskDto>>> GetTasks(
        string tenantId,
        [FromQuery] long? projectId,
        [FromQuery] string? status,
        [FromQuery] string? assigneeId,
        [FromQuery] string? priority,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        _tenantAccessor.SetTenant(tenantId);
        _logger.LogInformation("Fetching tasks for tenant {TenantId}", tenantId);

        var query = _context.Tasks.Where(t => t.TenantId == tenantId);

        if (projectId.HasValue)
            query = query.Where(t => t.ProjectId == projectId.Value);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(t => t.Status == status);

        if (!string.IsNullOrEmpty(assigneeId))
            query = query.Where(t => t.AssigneeId == assigneeId);

        if (!string.IsNullOrEmpty(priority))
            query = query.Where(t => t.Priority == priority);

        var tasks = await query
            .OrderByDescending(t => t.Priority == "Critical" ? 4 :
                                   t.Priority == "High" ? 3 :
                                   t.Priority == "Medium" ? 2 : 1)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TaskDto
            {
                Id = t.Id,
                TenantId = t.TenantId,
                ProjectId = t.ProjectId,
                Title = t.Title,
                Description = t.Description,
                Status = t.Status,
                Priority = t.Priority,
                AssigneeId = t.AssigneeId,
                DueDate = t.DueDate,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        return Ok(tasks);
    }

    /// <summary>
    /// Get a specific task with comments.
    /// </summary>
    [HttpGet("{taskId}")]
    public async Task<ActionResult<TaskDetailDto>> GetTask(string tenantId, long taskId)
    {
        _tenantAccessor.SetTenant(tenantId);

        var task = await _context.Tasks
            .Where(t => t.TenantId == tenantId && t.Id == taskId)
            .FirstOrDefaultAsync();

        if (task == null)
            return NotFound();

        var comments = await _context.TaskComments
            .Where(c => c.TenantId == tenantId && c.TaskId == taskId && !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CommentDto
            {
                Id = c.Id,
                AuthorId = c.AuthorId,
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                EditedAt = c.EditedAt
            })
            .ToListAsync();

        return Ok(new TaskDetailDto
        {
            Id = task.Id,
            TenantId = task.TenantId,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            Priority = task.Priority,
            AssigneeId = task.AssigneeId,
            ReporterId = task.ReporterId,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            CompletedAt = task.CompletedAt,
            EstimatedHours = task.EstimatedHours,
            Comments = comments
        });
    }

    /// <summary>
    /// Create a new task.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TaskDto>> CreateTask(string tenantId, CreateTaskRequest request)
    {
        _tenantAccessor.SetTenant(tenantId);
        _logger.LogInformation("Creating task for tenant {TenantId}", tenantId);

        var task = new ProjectTask
        {
            TenantId = tenantId,
            ProjectId = request.ProjectId,
            Title = request.Title,
            Description = request.Description,
            Status = "Todo",
            Priority = request.Priority ?? "Medium",
            AssigneeId = request.AssigneeId,
            ReporterId = request.ReporterId,
            DueDate = request.DueDate,
            EstimatedHours = request.EstimatedHours,
            CreatedAt = DateTime.UtcNow
        };

        _context.Tasks.Add(task);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTask), new { tenantId, taskId = task.Id }, new TaskDto
        {
            Id = task.Id,
            TenantId = task.TenantId,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            Priority = task.Priority,
            AssigneeId = task.AssigneeId,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt
        });
    }

    /// <summary>
    /// Update a task.
    /// </summary>
    [HttpPut("{taskId}")]
    public async Task<ActionResult<TaskDto>> UpdateTask(
        string tenantId, long taskId, UpdateTaskRequest request)
    {
        _tenantAccessor.SetTenant(tenantId);

        var task = await _context.Tasks
            .Where(t => t.TenantId == tenantId && t.Id == taskId)
            .FirstOrDefaultAsync();

        if (task == null)
            return NotFound();

        if (request.Title != null)
            task.Title = request.Title;
        if (request.Description != null)
            task.Description = request.Description;
        if (request.Status != null)
        {
            task.Status = request.Status;
            if (request.Status == "Done")
                task.CompletedAt = DateTime.UtcNow;
        }
        if (request.Priority != null)
            task.Priority = request.Priority;
        if (request.AssigneeId != null)
            task.AssigneeId = request.AssigneeId;
        if (request.DueDate.HasValue)
            task.DueDate = request.DueDate;

        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new TaskDto
        {
            Id = task.Id,
            TenantId = task.TenantId,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            Priority = task.Priority,
            AssigneeId = task.AssigneeId,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt
        });
    }

    /// <summary>
    /// Add a comment to a task.
    /// </summary>
    [HttpPost("{taskId}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(
        string tenantId, long taskId, CreateCommentRequest request)
    {
        _tenantAccessor.SetTenant(tenantId);

        var taskExists = await _context.Tasks
            .AnyAsync(t => t.TenantId == tenantId && t.Id == taskId);

        if (!taskExists)
            return NotFound();

        var comment = new TaskComment
        {
            TenantId = tenantId,
            TaskId = taskId,
            AuthorId = request.AuthorId,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow
        };

        _context.TaskComments.Add(comment);
        await _context.SaveChangesAsync();

        return Ok(new CommentDto
        {
            Id = comment.Id,
            AuthorId = comment.AuthorId,
            Content = comment.Content,
            CreatedAt = comment.CreatedAt
        });
    }

    /// <summary>
    /// Get task board view (Kanban style).
    /// </summary>
    [HttpGet("board/{projectId}")]
    public async Task<ActionResult<TaskBoardDto>> GetTaskBoard(string tenantId, long projectId)
    {
        _tenantAccessor.SetTenant(tenantId);
        _logger.LogInformation(
            "Loading task board for project {ProjectId} in tenant {TenantId}",
            projectId, tenantId);

        var tasks = await _context.Tasks
            .Where(t => t.TenantId == tenantId && t.ProjectId == projectId)
            .Select(t => new TaskDto
            {
                Id = t.Id,
                TenantId = t.TenantId,
                ProjectId = t.ProjectId,
                Title = t.Title,
                Status = t.Status,
                Priority = t.Priority,
                AssigneeId = t.AssigneeId,
                DueDate = t.DueDate,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        return Ok(new TaskBoardDto
        {
            ProjectId = projectId,
            TenantId = tenantId,
            Todo = tasks.Where(t => t.Status == "Todo").ToList(),
            InProgress = tasks.Where(t => t.Status == "InProgress").ToList(),
            Review = tasks.Where(t => t.Status == "Review").ToList(),
            Done = tasks.Where(t => t.Status == "Done").ToList()
        });
    }
}

// DTOs
public record TaskDto
{
    public long Id { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public long ProjectId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string? AssigneeId { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record TaskDetailDto : TaskDto
{
    public string? ReporterId { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? EstimatedHours { get; init; }
    public IEnumerable<CommentDto> Comments { get; init; } = [];
}

public record CreateTaskRequest
{
    public required long ProjectId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Priority { get; init; }
    public string? AssigneeId { get; init; }
    public string? ReporterId { get; init; }
    public DateTime? DueDate { get; init; }
    public int? EstimatedHours { get; init; }
}

public record UpdateTaskRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Status { get; init; }
    public string? Priority { get; init; }
    public string? AssigneeId { get; init; }
    public DateTime? DueDate { get; init; }
}

public record CommentDto
{
    public long Id { get; init; }
    public string AuthorId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? EditedAt { get; init; }
}

public record CreateCommentRequest
{
    public required string AuthorId { get; init; }
    public required string Content { get; init; }
}

public record TaskBoardDto
{
    public long ProjectId { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public IEnumerable<TaskDto> Todo { get; init; } = [];
    public IEnumerable<TaskDto> InProgress { get; init; } = [];
    public IEnumerable<TaskDto> Review { get; init; } = [];
    public IEnumerable<TaskDto> Done { get; init; } = [];
}

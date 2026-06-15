using Finora.Api.Extensions;
using Finora.Application.DTOs.Notification;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationRepository _notificationRepo;
    private readonly IHouseholdService _householdService;

    public NotificationsController(
        INotificationRepository notificationRepo,
        IHouseholdService householdService)
    {
        _notificationRepo = notificationRepo;
        _householdService = householdService;
    }

    private async Task<(Guid HouseholdId, Guid UserId)?> ResolveIdsAsync(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is not { } uid) return null;
        var h = await _householdService.GetOrCreateForUserAsync(uid, ct);
        if (h == null) return null;
        return (h.Id, uid);
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var ids = await ResolveIdsAsync(ct);
        if (ids == null) return NotFound();

        if (limit < 1) limit = 1;
        if (limit > 50) limit = 50;
        if (offset < 0) offset = 0;

        var notifications = await _notificationRepo.GetByHouseholdAsync(ids.Value.HouseholdId, ids.Value.UserId, limit, offset, ct);

        var dtos = notifications.Select(n => new NotificationDto
        {
            Id = n.Id,
            Type = (int)n.Type,
            Message = n.Message,
            RedirectUrl = n.RedirectUrl,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        var ids = await ResolveIdsAsync(ct);
        if (ids == null) return NotFound();

        var count = await _notificationRepo.GetUnreadCountAsync(ids.Value.HouseholdId, ids.Value.UserId, ct);
        return Ok(new { count });
    }

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        var ids = await ResolveIdsAsync(ct);
        if (ids == null) return NotFound();

        await _notificationRepo.MarkAsReadAsync(id, ids.Value.HouseholdId, ct);
        return NoContent();
    }

    [HttpPost("mark-batch-read")]
    public async Task<IActionResult> MarkBatchAsRead([FromBody] MarkBatchReadRequest request, CancellationToken ct)
    {
        var ids = await ResolveIdsAsync(ct);
        if (ids == null) return NotFound();

        if (request.Ids == null || request.Ids.Count == 0) return BadRequest();

        await _notificationRepo.MarkBatchAsReadAsync(request.Ids, ids.Value.HouseholdId, ct);
        return NoContent();
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        var ids = await ResolveIdsAsync(ct);
        if (ids == null) return NotFound();

        await _notificationRepo.MarkAllAsReadAsync(ids.Value.HouseholdId, ids.Value.UserId, ct);
        return NoContent();
    }
}

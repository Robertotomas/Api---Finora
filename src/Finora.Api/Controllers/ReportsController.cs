using Finora.Api.Extensions;
using Finora.Application.DTOs.Reports;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IMonthlyReportRepository _monthlyReportRepository;
    private readonly IMonthlyReportGenerationService _generationService;
    private readonly IHouseholdService _householdService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IHostEnvironment _hostEnvironment;

    public ReportsController(
        IMonthlyReportRepository monthlyReportRepository,
        IMonthlyReportGenerationService generationService,
        IHouseholdService householdService,
        ISubscriptionService subscriptionService,
        IHostEnvironment hostEnvironment)
    {
        _monthlyReportRepository = monthlyReportRepository;
        _generationService = generationService;
        _householdService = householdService;
        _subscriptionService = subscriptionService;
        _hostEnvironment = hostEnvironment;
    }

    private Guid? UserId => User.GetUserId();

    private async Task<Guid?> ResolveHouseholdIdAsync(CancellationToken cancellationToken)
    {
        if (UserId is not { } userId)
            return null;
        var household = await _householdService.GetOrCreateForUserAsync(userId, cancellationToken);
        return household?.Id;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MonthlyReportListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<MonthlyReportListItemDto>>> List(
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken cancellationToken)
    {
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessMonthlyReportsAsync(householdId.Value, cancellationToken))
            return Forbid();

        var rows = await _monthlyReportRepository.ListByHouseholdAsync(householdId.Value, year, month, cancellationToken);
        var dtos = rows.Select(r => new MonthlyReportListItemDto
        {
            Id = r.Id,
            Year = r.Year,
            Month = r.Month,
            GeneratedAt = r.GeneratedAt,
            FileSizeBytes = r.FileSizeBytes
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessMonthlyReportsAsync(householdId.Value, cancellationToken))
            return Forbid();

        var report = await _monthlyReportRepository.GetByIdAsync(id, cancellationToken);
        if (report == null || report.HouseholdId != householdId.Value)
            return NotFound();

        var fullPath = Path.Combine(_hostEnvironment.ContentRootPath, "uploads", report.FileRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var stream = System.IO.File.OpenRead(fullPath);
        var fileName = $"finora-relatorio-{report.Year}-{report.Month:00}.pdf";
        return File(stream, "application/pdf", fileName);
    }

    [HttpPost("{id:guid}/refresh")]
    [ProducesResponseType(typeof(MonthlyReportListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MonthlyReportListItemDto>> Refresh(Guid id, CancellationToken cancellationToken)
    {
        if (UserId is not { } userId)
            return Unauthorized();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessMonthlyReportsAsync(householdId.Value, cancellationToken))
            return Forbid();

        var dto = await _generationService.RegenerateReportAsync(id, householdId.Value, userId, cancellationToken);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Debug: generate PDF for a month (Development only).</summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateDebug(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken)
    {
        if (!_hostEnvironment.IsDevelopment())
            return NotFound();

        if (UserId is not { } userId)
            return Unauthorized();

        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null)
            return NotFound();

        if (!await _subscriptionService.CanAccessMonthlyReportsAsync(householdId.Value, cancellationToken))
            return Forbid();

        var id = await _generationService.GenerateForHouseholdMonthAsync(householdId.Value, userId, year, month, cancellationToken);
        if (id == null)
            return BadRequest(new { message = "Não foi possível gerar o relatório." });

        return Ok(new { id });
    }
}

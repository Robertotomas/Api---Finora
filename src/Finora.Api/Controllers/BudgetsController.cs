using Finora.Api.Extensions;
using Finora.Application.DTOs.Budget;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BudgetsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHouseholdService _householdService;

    public BudgetsController(ApplicationDbContext db, IHouseholdService householdService)
    {
        _db = db;
        _householdService = householdService;
    }

    private async Task<Guid?> ResolveHouseholdIdAsync(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is not { } uid) return null;
        var h = await _householdService.GetOrCreateForUserAsync(uid, ct);
        return h?.Id;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MonthlyBudgetDto>>> List(
        [FromQuery] int? year,
        CancellationToken ct)
    {
        var hid = await ResolveHouseholdIdAsync(ct);
        if (hid == null) return NotFound();

        var query = _db.MonthlyBudgets
            .AsNoTracking()
            .Where(b => b.HouseholdId == hid.Value);

        if (year.HasValue)
            query = query.Where(b => b.Year == year.Value);

        var rows = await query
            .OrderBy(b => b.Year).ThenBy(b => b.Month)
            .Select(b => new MonthlyBudgetDto
            {
                Id = b.Id,
                Year = b.Year,
                Month = b.Month,
                ExpectedIncome = b.ExpectedIncome,
                ExpectedExpenses = b.ExpectedExpenses
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpPut]
    public async Task<ActionResult<MonthlyBudgetDto>> Upsert(
        [FromBody] UpsertMonthlyBudgetRequest request,
        CancellationToken ct)
    {
        var hid = await ResolveHouseholdIdAsync(ct);
        if (hid == null) return NotFound();

        var existing = await _db.MonthlyBudgets
            .FirstOrDefaultAsync(b => b.HouseholdId == hid.Value && b.Year == request.Year && b.Month == request.Month, ct);

        if (existing != null)
        {
            existing.ExpectedIncome = request.ExpectedIncome;
            existing.ExpectedExpenses = request.ExpectedExpenses;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new MonthlyBudget
            {
                Id = Guid.NewGuid(),
                HouseholdId = hid.Value,
                Year = request.Year,
                Month = request.Month,
                ExpectedIncome = request.ExpectedIncome,
                ExpectedExpenses = request.ExpectedExpenses,
                CreatedAt = DateTime.UtcNow
            };
            _db.MonthlyBudgets.Add(existing);
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new MonthlyBudgetDto
        {
            Id = existing.Id,
            Year = existing.Year,
            Month = existing.Month,
            ExpectedIncome = existing.ExpectedIncome,
            ExpectedExpenses = existing.ExpectedExpenses
        });
    }

    [HttpDelete("{year:int}/{month:int}")]
    public async Task<IActionResult> Delete(int year, int month, CancellationToken ct)
    {
        var hid = await ResolveHouseholdIdAsync(ct);
        if (hid == null) return NotFound();

        var existing = await _db.MonthlyBudgets
            .FirstOrDefaultAsync(b => b.HouseholdId == hid.Value && b.Year == year && b.Month == month, ct);

        if (existing == null) return NotFound();

        _db.MonthlyBudgets.Remove(existing);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}

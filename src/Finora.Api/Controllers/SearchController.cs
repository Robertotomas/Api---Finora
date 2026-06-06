using Finora.Api.Extensions;
using Finora.Application.DTOs.Search;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;
    private readonly IHouseholdService _householdService;

    public SearchController(ISearchService searchService, IHouseholdService householdService)
    {
        _searchService = searchService;
        _householdService = householdService;
    }

    private Guid? UserId => User.GetUserId();

    [HttpGet]
    [ProducesResponseType(typeof(GlobalSearchResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GlobalSearchResultDto>> Search([FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (UserId is not { } userId)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new GlobalSearchResultDto());

        var household = await _householdService.GetOrCreateForUserAsync(userId, cancellationToken);
        if (household == null)
            return Ok(new GlobalSearchResultDto());

        var result = await _searchService.SearchAsync(household.Id, q.Trim(), cancellationToken);
        return Ok(result);
    }
}

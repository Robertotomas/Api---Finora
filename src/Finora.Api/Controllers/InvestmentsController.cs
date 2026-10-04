using System.Security.Claims;
using Finora.Application.DTOs.Investment;
using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvestmentsController : ControllerBase
{
    private readonly IInvestmentService _investmentService;
    private readonly IHouseholdService _householdService;
    private readonly ISubscriptionService _subscriptionService;

    public InvestmentsController(
        IInvestmentService investmentService,
        IHouseholdService householdService,
        ISubscriptionService subscriptionService)
    {
        _investmentService = investmentService;
        _householdService = householdService;
        _subscriptionService = subscriptionService;
    }

    private const string LockedMessage = "Os investimentos estão disponíveis nos planos Pro e Couple.";

    private Guid? UserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var guid) ? guid : null;
        }
    }

    private Guid? HouseholdIdFromClaim
    {
        get
        {
            var id = User.FindFirstValue("household_id");
            return !string.IsNullOrEmpty(id) && Guid.TryParse(id, out var guid) ? guid : null;
        }
    }

    private async Task<Guid?> ResolveHouseholdIdAsync(CancellationToken cancellationToken)
    {
        if (HouseholdIdFromClaim is { } id) return id;
        if (UserId is not { } userId) return null;
        var household = await _householdService.GetOrCreateForUserAsync(userId, cancellationToken);
        return household?.Id;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<InvestmentHoldingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InvestmentHoldingDto>>> GetAll(CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        var items = await _investmentService.GetByHouseholdAsync(householdId.Value, UserId!.Value, cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvestmentHoldingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvestmentHoldingDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var item = await _investmentService.GetByIdAsync(id, UserId.Value, cancellationToken);
        return item == null ? NotFound() : Ok(item);
    }

    /// <summary>Pesquisa de instrumentos (autocomplete de tickers).</summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<InstrumentSearchResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InstrumentSearchResultDto>>> Search([FromQuery] string q, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        if (!await _subscriptionService.CanAccessInvestmentsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "INVESTMENTS_LOCKED", message = LockedMessage });

        var results = await _investmentService.SearchAsync(q ?? string.Empty, cancellationToken);
        return Ok(results);
    }

    /// <summary>Série de valor da carteira inteira (EUR) para o gráfico de evolução.</summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(InvestmentHistoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvestmentHistoryDto>> GetHouseholdHistory([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        var history = await _investmentService.GetHouseholdHistoryAsync(householdId.Value, UserId.Value, ParseDate(from), ParseDate(to), cancellationToken);
        return Ok(history);
    }

    /// <summary>Total líquido depositado na corretora (EUR) — métrica "Depósitos".</summary>
    [HttpGet("deposits")]
    [ProducesResponseType(typeof(InvestmentDepositsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvestmentDepositsDto>> GetDeposits(CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        var summary = await _investmentService.GetDepositsSummaryAsync(householdId.Value, UserId.Value, cancellationToken);
        return Ok(summary);
    }

    /// <summary>Adiciona um depósito à mão; debita opcionalmente uma conta. Devolve o total atualizado.</summary>
    [HttpPost("deposits")]
    [ProducesResponseType(typeof(InvestmentDepositsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvestmentDepositsDto>> AddDeposit([FromBody] AddDepositRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        var summary = await _investmentService.AddManualDepositAsync(request, householdId.Value, UserId.Value, cancellationToken);
        return Ok(summary);
    }

    /// <summary>Edita um depósito (reconcilia o saldo da conta). Devolve o total atualizado.</summary>
    [HttpPut("deposits/{id:guid}")]
    [ProducesResponseType(typeof(InvestmentDepositsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvestmentDepositsDto>> UpdateDeposit(Guid id, [FromBody] UpdateDepositRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        var summary = await _investmentService.UpdateDepositAsync(id, request, householdId.Value, UserId.Value, cancellationToken);
        return Ok(summary);
    }

    /// <summary>Elimina um depósito (devolve ao saldo o que tinha debitado). Devolve o total atualizado.</summary>
    [HttpDelete("deposits/{id:guid}")]
    [ProducesResponseType(typeof(InvestmentDepositsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvestmentDepositsDto>> DeleteDeposit(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        var summary = await _investmentService.DeleteDepositAsync(id, householdId.Value, UserId.Value, cancellationToken);
        return Ok(summary);
    }

    /// <summary>Série de valor de uma posição (EUR) para o gráfico de evolução.</summary>
    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(InvestmentHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvestmentHistoryDto>> GetHoldingHistory(Guid id, [FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var history = await _investmentService.GetHoldingHistoryAsync(id, UserId.Value, ParseDate(from), ParseDate(to), cancellationToken);
        return history == null ? NotFound() : Ok(history);
    }

    /// <summary>Cotação histórica de um ticker (na moeda do instrumento) — pré-visualização no modal.</summary>
    [HttpGet("quote-history")]
    [ProducesResponseType(typeof(InstrumentPriceHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InstrumentPriceHistoryDto>> GetInstrumentHistory([FromQuery] string symbol, [FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        if (!await _subscriptionService.CanAccessInvestmentsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "INVESTMENTS_LOCKED", message = LockedMessage });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var toDate = ParseDate(to) ?? today;
        var fromDate = ParseDate(from) ?? toDate.AddYears(-1);
        var history = await _investmentService.GetInstrumentHistoryAsync(symbol ?? string.Empty, fromDate, toDate, cancellationToken);
        return Ok(history);
    }

    private static DateOnly? ParseDate(string? value)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Importa transações já parseadas no cliente (Excel/CSV da corretora). dryRun=true → só pré-visualização.</summary>
    [HttpPost("import")]
    [ProducesResponseType(typeof(InvestmentImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InvestmentImportResultDto>> Import([FromBody] BrokerImportRequest request, [FromQuery] bool dryRun, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        if (!await _subscriptionService.CanAccessInvestmentsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "INVESTMENTS_LOCKED", message = LockedMessage });

        var result = await _investmentService.ImportTradesAsync(request ?? new BrokerImportRequest(), householdId.Value, UserId.Value, dryRun, cancellationToken);
        return Ok(result);
    }

    /// <summary>Adiciona uma transação (compra/venda); cria a posição se não existir.</summary>
    [HttpPost("transactions")]
    [ProducesResponseType(typeof(InvestmentHoldingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvestmentHoldingDto>> AddTransaction([FromBody] AddTransactionRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        if (!await _subscriptionService.CanAccessInvestmentsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "INVESTMENTS_LOCKED", message = LockedMessage });

        var item = await _investmentService.AddTransactionAsync(request, householdId.Value, UserId.Value, cancellationToken);
        return item == null ? NotFound() : Ok(item);
    }

    /// <summary>Edita uma transação.</summary>
    [HttpPut("transactions/{transactionId:guid}")]
    [ProducesResponseType(typeof(InvestmentHoldingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvestmentHoldingDto>> UpdateTransaction(Guid transactionId, [FromBody] UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        if (!await _subscriptionService.CanAccessInvestmentsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "INVESTMENTS_LOCKED", message = LockedMessage });

        var item = await _investmentService.UpdateTransactionAsync(transactionId, request, UserId.Value, cancellationToken);
        return item == null ? NotFound() : Ok(item);
    }

    /// <summary>Elimina uma transação (a posição é removida se ficar sem transações).</summary>
    [HttpDelete("transactions/{transactionId:guid}")]
    [ProducesResponseType(typeof(InvestmentHoldingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<InvestmentHoldingDto>> DeleteTransaction(Guid transactionId, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var item = await _investmentService.DeleteTransactionAsync(transactionId, UserId.Value, cancellationToken);
        return item == null ? NoContent() : Ok(item);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var deleted = await _investmentService.DeleteAsync(id, UserId.Value, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Atualiza as cotações das posições deste agregado (refresh manual).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(IReadOnlyList<InvestmentHoldingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<InvestmentHoldingDto>>> Refresh(CancellationToken cancellationToken)
    {
        if (UserId == null) return NotFound();
        var householdId = await ResolveHouseholdIdAsync(cancellationToken);
        if (householdId == null) return NotFound();
        if (!await _subscriptionService.CanAccessInvestmentsAsync(householdId.Value, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "INVESTMENTS_LOCKED", message = LockedMessage });

        var items = await _investmentService.RefreshHouseholdQuotesAsync(householdId.Value, UserId.Value, cancellationToken);
        return Ok(items);
    }
}

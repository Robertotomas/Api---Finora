using Finora.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Finora.Api.Controllers;

/// <summary>
/// Stripe webhook receiver. Anonymous (Stripe is unauthenticated) but gated by signature verification —
/// only Stripe can produce a valid signature for our webhook secret. This is the durable source of truth
/// for plan changes; the post-checkout sync is just a best-effort fast path.
/// </summary>
[ApiController]
[Route("api/stripe")]
[AllowAnonymous]
[DisableRateLimiting]
public class StripeWebhookController : ControllerBase
{
    private readonly IStripeService _stripeService;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(IStripeService stripeService, ILogger<StripeWebhookController> logger)
    {
        _stripeService = stripeService;
        _logger = logger;
    }

    [HttpPost("webhooks")]
    public async Task<IActionResult> Handle(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();

        try
        {
            await _stripeService.HandleWebhookAsync(json, signature, cancellationToken);
            return Ok();
        }
        catch (Stripe.StripeException ex)
        {
            // Bad/forged signature or malformed event — reject so Stripe surfaces the failure.
            _logger.LogWarning(ex, "Stripe webhook signature verification failed");
            return BadRequest();
        }
        catch (Exception ex)
        {
            // Unexpected processing error — 500 makes Stripe retry the delivery.
            _logger.LogError(ex, "Stripe webhook processing failed");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

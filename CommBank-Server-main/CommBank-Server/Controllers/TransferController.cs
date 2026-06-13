using CommBank.Observability;
using CommBank.Transfers;
using CommBank.Transfers.Abstractions;
using CommBank.Transfers.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CommBank.Controllers;

/// <summary>
/// Fund-transfer endpoint. Thin HTTP edge over <see cref="IFundTransferService"/>: it forwards the
/// Idempotency-Key header and maps each typed transfer failure to an RFC 7807 ProblemDetails response.
/// </summary>
[ApiController]
[Authorize]
[Route("api/Transfers")]
public class TransferController : ControllerBase
{
    private readonly IFundTransferService _transferService;
    private readonly TransferOptions _options;
    private readonly ILogger<TransferController> _logger;

    public TransferController(
        IFundTransferService transferService,
        IOptions<TransferOptions> options,
        ILogger<TransferController> logger)
    {
        _transferService = transferService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Execute a fund transfer. Send a unique <c>Idempotency-Key</c> header to make retries safe.</summary>
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] TransferRequest request)
    {
        string? idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var values)
            ? values.ToString()
            : null;

        // Step-up: high-value transfers require a fresh MFA elevation (the acr=mfa claim).
        if (request.Amount >= _options.StepUpAmountThreshold && !User.HasClaim("acr", "mfa"))
        {
            var stepUp = new ProblemDetails
            {
                Title = "Step-up authentication required",
                Detail = $"Transfers of {_options.StepUpAmountThreshold:N2} or more require step-up (MFA). " +
                         "Call POST /api/Auth/step-up with a current code, then retry with the elevated token.",
                Status = StatusCodes.Status403Forbidden
            };
            stepUp.Extensions["code"] = "step_up_required";
            return StatusCode(StatusCodes.Status403Forbidden, stepUp);
        }

        try
        {
            TransferResult result = await _transferService.TransferAsync(request, idempotencyKey, HttpContext.RequestAborted);

            // Record business metrics only for fresh executions, never idempotent replays.
            if (!result.Idempotent)
            {
                AppDiagnostics.TransfersCompleted.Add(1);
                AppDiagnostics.TransferAmount.Record((double)result.Amount);
            }

            return result.Idempotent
                ? Ok(result)
                : Created($"/api/Transaction/{result.DebitTransactionId}", result);
        }
        catch (InvalidTransferException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid transfer");
        }
        catch (AccountNotFoundException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Account not found");
        }
        catch (InsufficientFundsException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Insufficient funds");
        }
        catch (TransferBlockedException ex)
        {
            var problem = new ProblemDetails
            {
                Title = "Transfer blocked by risk policy",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            };
            problem.Extensions["riskScore"] = ex.Assessment.Score;
            problem.Extensions["riskBand"] = ex.Assessment.Band.ToString();
            problem.Extensions["reasons"] = ex.Assessment.Reasons.Select(r => r.Code).ToArray();

            AppDiagnostics.TransfersBlocked.Add(1);
            _logger.LogWarning("Transfer blocked by risk policy for user {UserId} (score {Score}).", request.UserId, ex.Assessment.Score);
            return StatusCode(StatusCodes.Status422UnprocessableEntity, problem);
        }
        catch (ConcurrencyConflictException ex)
        {
            // Retries were exhausted under contention; the caller may safely retry with the same idempotency key.
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Concurrency conflict");
        }
    }
}

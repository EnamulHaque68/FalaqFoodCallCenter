using CallCenter.Application.Reports;
using CallCenter.Application.Reports.DTOs;
using CallCenter.Domain.Security;
using CallCenter.Infrastructure.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = AppPermissions.ReportsView)]
public sealed class ReportsController(IReportService reportService) : ControllerBase
{
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(ReportMetricsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReportMetricsDto>> GetMetrics(CancellationToken cancellationToken)
        => Ok(await reportService.GetMetricsAsync(cancellationToken));

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(OperationsDashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<OperationsDashboardDto>> GetDashboard(CancellationToken cancellationToken)
        => Ok(await reportService.GetOperationsDashboardAsync(cancellationToken));

    [HttpGet("dispositions")]
    [ProducesResponseType(typeof(DispositionReportDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DispositionReportDto>> GetDispositionReport(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
        => Ok(await reportService.GetDispositionReportAsync(fromUtc, toUtc, cancellationToken));

    [HttpGet("analytics")]
    [ProducesResponseType(typeof(ComprehensiveAnalyticsReportDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ComprehensiveAnalyticsReportDto>> GetAnalytics(
        [FromQuery] string? preset,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
        => Ok(await reportService.GetAnalyticsReportAsync(preset, fromUtc, toUtc, cancellationToken));

    [HttpGet("export/calls")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportCalls(
        [FromQuery] string? preset,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var csvBytes = await reportService.ExportCallReportCsvAsync(preset, fromUtc, toUtc, cancellationToken);
            var filename = $"calls-analytics-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            return File(csvBytes, "text/csv", filename);
        }
        catch (ReportExportLimitExceededException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Export is too large", Detail = exception.Message });
        }
    }
}

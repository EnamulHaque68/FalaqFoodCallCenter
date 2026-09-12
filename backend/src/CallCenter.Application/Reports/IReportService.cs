using CallCenter.Application.Reports.DTOs;

namespace CallCenter.Application.Reports;

public interface IReportService
{
    Task<ReportMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default);
    Task<OperationsDashboardDto> GetOperationsDashboardAsync(CancellationToken cancellationToken = default);
    Task<DispositionReportDto> GetDispositionReportAsync(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<ComprehensiveAnalyticsReportDto> GetAnalyticsReportAsync(
        string? preset = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<byte[]> ExportCallReportCsvAsync(
        string? preset = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);
}

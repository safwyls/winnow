using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;

namespace Winnow.App.Services;

/// <inheritdoc cref="IWorkMetadataEditService"/>
public sealed class WorkMetadataEditService : IWorkMetadataEditService
{
    private readonly IWorkRepository _works;
    private readonly IWorkFieldSourceRepository _fields;
    private readonly IWorkIgdbPinRepository? _pins;
    private readonly UserArtStore? _art;
    private readonly ILogger<WorkMetadataEditService> _log;

    public WorkMetadataEditService(
        IWorkRepository works,
        IWorkFieldSourceRepository fields,
        IWorkIgdbPinRepository? pins = null,
        UserArtStore? art = null,
        ILogger<WorkMetadataEditService>? log = null)
    {
        _works = works;
        _fields = fields;
        _pins = pins;
        _art = art;
        _log = log ?? NullLogger<WorkMetadataEditService>.Instance;
    }

    public async Task<WorkMetadataSnapshot?> GetAsync(long workId, CancellationToken ct = default)
    {
        try
        {
            var work = await _works.GetAsync(workId, ct);
            if (work is null)
            {
                return null;
            }

            var states = await _fields.GetStateAsync(workId, ct);
            var pin = _pins is null ? null : await _pins.GetAsync(workId, ct);

            return new WorkMetadataSnapshot(
                workId,
                work.Name,
                pin is not null,
                [.. states.Select(s => new WorkMetadataField(s.Field, s.Value, s.Source))]);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading the editable metadata of work {WorkId} failed.", workId);
            return null;
        }
    }

    public async Task<WorkFieldEditOutcome> SetFieldAsync(
        long workId, string field, string? value, CancellationToken ct = default)
    {
        try
        {
            return await _fields.SetFieldAsync(workId, field, value, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Setting {Field} on work {WorkId} failed.", field, workId);
            return WorkFieldEditOutcome.InvalidValue;
        }
    }

    public async Task<WorkFieldEditOutcome> ResetFieldAsync(
        long workId, string field, CancellationToken ct = default)
    {
        try
        {
            return await _fields.ResetFieldAsync(workId, field, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Resetting {Field} on work {WorkId} failed.", field, workId);
            return WorkFieldEditOutcome.InvalidValue;
        }
    }

    public Task<WorkArtEditOutcome> SetArtFromFileAsync(
        long workId, string field, string filePath, CancellationToken ct = default)
        => ImportArtAsync(workId, field, store => store.ImportFileAsync(filePath, ct), ct);

    public Task<WorkArtEditOutcome> SetArtFromUrlAsync(
        long workId, string field, string url, CancellationToken ct = default)
        => ImportArtAsync(workId, field, store => store.ImportUrlAsync(url, ct), ct);

    public CoverKey? ArtKeyFor(string? value) => ArtKeys.Resolve(value);

    private async Task<WorkArtEditOutcome> ImportArtAsync(
        long workId,
        string field,
        Func<UserArtStore, Task<UserArtImport>> import,
        CancellationToken ct)
    {
        if (!WorkFields.IsArt(field))
        {
            return WorkArtEditOutcome.UnknownField;
        }

        if (_art is null)
        {
            return WorkArtEditOutcome.Failed;
        }

        try
        {
            var imported = await import(_art);
            if (imported.Reference is null)
            {
                return imported.Failure switch
                {
                    UserArtImportFailure.FileNotFound => WorkArtEditOutcome.FileNotFound,
                    UserArtImportFailure.Unreadable => WorkArtEditOutcome.Unreadable,
                    UserArtImportFailure.TooLarge => WorkArtEditOutcome.TooLarge,
                    UserArtImportFailure.NotAnImage => WorkArtEditOutcome.NotAnImage,
                    UserArtImportFailure.BadUrl => WorkArtEditOutcome.BadUrl,
                    UserArtImportFailure.DownloadFailed => WorkArtEditOutcome.DownloadFailed,
                    _ => WorkArtEditOutcome.Failed,
                };
            }

            var applied = await _fields.SetFieldAsync(workId, field, imported.Reference, ct);
            return applied switch
            {
                WorkFieldEditOutcome.Applied => WorkArtEditOutcome.Applied,
                WorkFieldEditOutcome.WorkNotFound => WorkArtEditOutcome.WorkNotFound,
                WorkFieldEditOutcome.UnknownField => WorkArtEditOutcome.UnknownField,
                _ => WorkArtEditOutcome.Failed,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Importing {Field} art for work {WorkId} failed.", field, workId);
            return WorkArtEditOutcome.Failed;
        }
    }
}

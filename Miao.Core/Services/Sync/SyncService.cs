using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Services;
using Miao.Core.Models;

namespace Miao.Core.Services.Sync
{
    public class SyncService
    {
        private readonly MiaoDbContext _db;
        private readonly IDriveFileStore _drive;
        private readonly string _deviceId;

        private const int MaxRetries = 4;
        private static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30),
        };

        public SyncService(MiaoDbContext db, IDriveFileStore drive, string deviceId)
        {
            _db = db;
            _drive = drive;
            _deviceId = deviceId;
        }

        public async Task<SyncResult> SyncNowAsync(CancellationToken ct = default)
        {
            var pushResult = await PushPendingAsync(ct);
            var pullResult = await PullRemoteChangesAsync(ct);

            return new SyncResult(
                Pushed: pushResult.Succeeded,
                PushFailed: pushResult.Failed,
                Pulled: pullResult.Applied,
                PullFailed: pullResult.Failed);
        }

        private async Task<(int Succeeded, int Failed)> PushPendingAsync(CancellationToken ct)
        {
            var pending = await _db.Set<PendingSync>()
                .OrderBy(p => p.Timestamp)
                .ToListAsync(ct);

            int succeeded = 0, failed = 0;

            foreach (var item in pending)
            {
                ct.ThrowIfCancellationRequested();

                var envelope = await BuildEnvelopeAsync(item, ct);
                if (envelope is null)
                {
                    _db.Set<PendingSync>().Remove(item);
                    continue;
                }

                var relativePath = BuildPath(envelope.EntityType, envelope.EntityId);
                var json = JsonSerializer.Serialize(envelope);

                var ok = await UploadWithRetryAsync(relativePath, json, ct);

                if (ok)
                {
                    _db.Set<PendingSync>().Remove(item);
                    await _db.SaveChangesAsync(ct);
                    succeeded++;
                }
                else
                {
                    failed++;
                    break;
                }
            }

            return (succeeded, failed);
        }

        private async Task<bool> UploadWithRetryAsync(string path, string content, CancellationToken ct)
        {
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var success = await _drive.UploadAsync(path, content, ct);
                    if (success) return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    
                }

                if (attempt < RetryDelays.Length)
                    await Task.Delay(RetryDelays[attempt], ct);
            }

            return false;
        }

        private async Task<(int Applied, int Failed)> PullRemoteChangesAsync(CancellationToken ct)
        {
            var remoteFiles = await _drive.ListAsync("entities/", ct);
            int applied = 0, failed = 0;

            foreach (var file in remoteFiles)
            {
                ct.ThrowIfCancellationRequested();

                string? json;
                try
                {
                    json = await _drive.DownloadAsync(file.RelativePath, ct);
                }
                catch
                {
                    failed++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(json)) continue;

                SyncEnvelope? envelope;
                try
                {
                    envelope = JsonSerializer.Deserialize<SyncEnvelope>(json);
                }
                catch
                {
                    failed++;
                    continue;
                }

                if (envelope is null || envelope.DeviceId == _deviceId)
                    continue;

                await using var transaction = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var wasApplied = await ApplyEnvelopeAsync(envelope, ct);
                    if (wasApplied)
                    {
                        await _db.SaveChangesAsync(ct);
                        await transaction.CommitAsync(ct);
                        applied++;
                    }
                    else
                    {
                        await transaction.RollbackAsync(ct);
                    }
                }
                catch
                {
                    await transaction.RollbackAsync(ct);
                    failed++;
                }
            }

            return (applied, failed);
        }

        private async Task<bool> ApplyEnvelopeAsync(SyncEnvelope envelope, CancellationToken ct)
        {
            switch (envelope.EntityType)
            {
                case nameof(Novel):
                    return await ApplyEntityAsync<Novel>(envelope, ct);
                case nameof(Chapter):
                    return await ApplyEntityAsync<Chapter>(envelope, ct);
                case nameof(GlossarySet):
                    return await ApplyEntityAsync<GlossarySet>(envelope, ct);
                case nameof(GlossarySetEntry):
                    return await ApplyEntityAsync<GlossarySetEntry>(envelope, ct);
                case nameof(Character):
                    return await ApplyEntityAsync<Character>(envelope, ct);
                case nameof(CharacterGroup):
                    return await ApplyEntityAsync<CharacterGroup>(envelope, ct);
                case nameof(CharacterAlias):
                    return await ApplyEntityAsync<CharacterAlias>(envelope, ct);
                default:
                    return false;
            }
        }

        private async Task<bool> ApplyEntityAsync<T>(SyncEnvelope envelope, CancellationToken ct)
            where T : class
        {
            var set = _db.Set<T>();
            var existing = await set.FindAsync(new object[] { envelope.EntityId }, ct);

            if (envelope.IsDeleted)
            {
                if (existing != null) set.Remove(existing);
                return true;
            }

            var incoming = JsonSerializer.Deserialize<T>(envelope.PayloadJson);
            if (incoming is null) return false;

            if (existing is null)
            {
                set.Add(incoming);
                return true;
            }

            var localUpdatedAt = GetUpdatedAtUtc(existing);
            if (localUpdatedAt != null && localUpdatedAt >= envelope.UpdatedAtUtc)
                return false;

            _db.Entry(existing).CurrentValues.SetValues(incoming);
            return true;
        }

        private static DateTime? GetUpdatedAtUtc(object entity) => entity switch
        {
            Chapter c => c.LastEditedAt ?? c.DownloadedAt,
            Novel n => n.LastUpdatedAt ?? n.AddedAt,
            _ => null
        };

        private static string BuildPath(string entityType, Guid id) =>
            $"entities/{entityType}/{id}.json";

        private async Task<SyncEnvelope?> BuildEnvelopeAsync(PendingSync item, CancellationToken ct)
        {
            object? entity = item.EntityType switch
            {
                SyncEntityType.Novel => await _db.Novels.FindAsync(new object[] { item.EntityId }, ct),
                SyncEntityType.Chapter => await _db.Chapters.FindAsync(new object[] { item.EntityId }, ct),
                SyncEntityType.GlossarySet => await _db.GlossarySets.FindAsync(new object[] { item.EntityId }, ct),
                _ => null
            };

            if (entity is null && item.Action != SyncAction.Delete)
                return null;

            return new SyncEnvelope
            {
                EntityType = item.EntityType.ToString(),
                EntityId = item.EntityId,
                DeviceId = _deviceId,
                UpdatedAtUtc = DateTime.UtcNow,
                IsDeleted = item.Action == SyncAction.Delete,
                PayloadJson = entity != null ? JsonSerializer.Serialize(entity) : "{}"
            };
        }
    }

    public record SyncResult(int Pushed, int PushFailed, int Pulled, int PullFailed);
}
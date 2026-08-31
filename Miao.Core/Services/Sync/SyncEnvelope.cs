using System;

namespace Miao.Core.Services.Sync
{
    public class SyncEnvelope
    {
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
        public bool IsDeleted { get; set; }
        public string PayloadJson { get; set; } = string.Empty;
    }
}
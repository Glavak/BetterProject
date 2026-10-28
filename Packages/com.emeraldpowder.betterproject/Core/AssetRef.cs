using System;

namespace BetterProject.Core
{
    /// <summary>
    /// Identifies an asset by GUID plus optional sub-asset local id.
    /// LocalId == 0 means the main asset or a folder.
    /// </summary>
    public readonly struct AssetRef : IEquatable<AssetRef>
    {
        public string Guid { get; }
        public long LocalId { get; }
        public bool IsSubAsset => LocalId != 0;

        public AssetRef(string guid, long localId = 0)
        {
            if (!IsValidGuid(guid))
                throw new ArgumentException("Guid must be 32 lowercase hex characters.", nameof(guid));
            Guid = guid;
            LocalId = localId;
        }

        static bool IsValidGuid(string g)
        {
            if (g == null || g.Length != 32) return false;
            for (int i = 0; i < g.Length; i++)
            {
                char c = g[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        public bool Equals(AssetRef other) => Guid == other.Guid && LocalId == other.LocalId;
        public override bool Equals(object obj) => obj is AssetRef o && Equals(o);
        public override int GetHashCode() => unchecked(((Guid?.GetHashCode() ?? 0) * 397) ^ LocalId.GetHashCode());
        public static bool operator ==(AssetRef a, AssetRef b) => a.Equals(b);
        public static bool operator !=(AssetRef a, AssetRef b) => !a.Equals(b);
        public override string ToString() => Guid + ":" + LocalId;
    }
}

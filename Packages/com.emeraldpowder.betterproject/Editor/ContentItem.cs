namespace BetterProject.Editor
{
    /// <summary>One entry shown in the content panel. InstanceId is resolved lazily (0 = not yet resolved).</summary>
    public struct ContentItem
    {
        public string Path;
        public string Guid;
        public int InstanceId;
        public bool IsFolder;
        public bool IsSubAsset;
        public int Depth;
        public bool HasSubAssets;
        public string Name;
    }
}

namespace EOS.Core
{
    // AI implementation: Contract for cloud-persisted models supporting similarity comparison and server-authoritative conflict merging
    public interface IMergeableCloudData<T>
    {
        bool IsSimilarTo(T other);

        /// <summary>
        /// Merges <paramref name="serverData"/> into this local instance.
        /// Conflicting fields must resolve in favor of <paramref name="serverData"/>.
        /// </summary>
        void MergeFromServer(T serverData);
    }
}

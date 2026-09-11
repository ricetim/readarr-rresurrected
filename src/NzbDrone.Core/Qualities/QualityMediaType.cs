namespace NzbDrone.Core.Qualities
{
    /// <summary>
    /// Ebooks and audiobooks are ranked and cut off independently, and a file of one
    /// type never replaces a file of the other.
    /// </summary>
    public enum QualityMediaType
    {
        Ebook = 0,
        Audiobook = 1
    }
}

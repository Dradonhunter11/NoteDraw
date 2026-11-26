namespace NoteDraw.Services
{
    public enum VaultEntryKind
    {
        Folder,
        MarkdownNote,
        DrawingFile,
        ImageFile
    }

    /// <summary>
    /// Represents a folder, Markdown note (.md), or standalone vector drawing (.ndraw) in a Vault.
    /// </summary>
    public sealed record VaultEntry(
        string Name,
        string FullPath,
        string RelativePath,
        VaultEntryKind Kind,
        int Depth);

    /// <summary>
    /// Heading item extracted from a Markdown document for the Outline view.
    /// </summary>
    public sealed record OutlineItem(int Level, string Title, int LineNumber);
}

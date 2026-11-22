namespace AugMechanicalAscension.Official;
// File access is restricted to the offline check executable.
internal static partial class OfficialSaveReadbackApi
{
    public static OfficialSaveReadResult ReadFile(string path, int maxBytes = DefaultMaxBytes,
        int maxDepth = DefaultMaxDepth)
    {
        if (string.IsNullOrWhiteSpace(path) || maxBytes <= 0 || maxDepth <= 0)
            return new(OfficialSaveReadStatus.Invalid, reason: "Invalid path or read limits");
        try
        {
            var before = new FileInfo(path);
            if (!before.Exists) return new(OfficialSaveReadStatus.Missing);
            long length = before.Length;
            var modified = before.LastWriteTimeUtc;
            if (length > maxBytes) return new(OfficialSaveReadStatus.TooLarge);
            byte[] bytes;
            // Do not read concurrently with a writer or permit replacement on
            // platforms honoring sharing modes. No waiting/retry or native save call.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > maxBytes) return new(OfficialSaveReadStatus.TooLarge);
                if (stream.Length != length) return new(OfficialSaveReadStatus.ChangedDuringRead);
                bytes = new byte[checked((int)length)];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count == 0) return new(OfficialSaveReadStatus.ChangedDuringRead, read);
                    read += count;
                }
                if (stream.ReadByte() != -1) return new(OfficialSaveReadStatus.ChangedDuringRead, read);
                var after = new FileInfo(path);
                if (!after.Exists || after.Length != bytes.Length || after.LastWriteTimeUtc != modified)
                    return new(OfficialSaveReadStatus.ChangedDuringRead, read);
            }
            return ReadBytes(bytes, maxBytes, maxDepth);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(OfficialSaveReadStatus.Unavailable, reason: ex.GetType().Name); }
    }
}

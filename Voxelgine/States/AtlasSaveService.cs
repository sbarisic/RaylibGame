#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace Voxelgine.States;

internal enum AtlasSaveStatus
{
	Saved,
	NothingToSave,
	Conflict,
	Failed,
	RollbackFailed,
}

internal sealed record AtlasSaveResult(
	AtlasSaveStatus Status,
	string Message,
	IReadOnlyList<string> RecoveryPaths)
{
	internal bool Succeeded => Status is AtlasSaveStatus.Saved or AtlasSaveStatus.NothingToSave;
}

/// <summary>
/// Saves authoritative atlas documents with per-file backups and complete rollback on later failure.
/// </summary>
internal sealed class AtlasSaveService
{
	private readonly AtlasAssetPaths paths;
	private readonly Dictionary<string, byte[]> expectedHashes = new(StringComparer.OrdinalIgnoreCase);

	internal AtlasSaveService(AtlasAssetPaths paths, IEnumerable<AtlasImageDocument> documents)
	{
		this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
		ArgumentNullException.ThrowIfNull(documents);
		foreach (AtlasImageDocument document in documents)
		foreach (string target in GetTargets(document.RelativePath))
			expectedHashes[target] = HashFile(target);
	}

	internal AtlasSaveResult Save(IReadOnlyList<AtlasSaveDocument> documents, bool overwriteConflicts = false)
	{
		ArgumentNullException.ThrowIfNull(documents);
		if (documents.Count == 0)
			return new AtlasSaveResult(AtlasSaveStatus.NothingToSave, "No atlas changes to save.", Array.Empty<string>());
		if (!paths.CanWriteSource)
			return new AtlasSaveResult(AtlasSaveStatus.Failed, "No writable source asset root is available.", Array.Empty<string>());

		List<SaveTarget> targets;
		try
		{
			targets = BuildTargets(documents);
		}
		catch
		{
			foreach (AtlasSaveDocument document in documents)
				document.Dispose();
			throw;
		}
		if (!overwriteConflicts)
		{
			string conflict = targets.Select(static target => target.Path).FirstOrDefault(HasConflict);
			if (conflict != null)
			{
				foreach (AtlasSaveDocument document in documents)
					document.Dispose();
				return new AtlasSaveResult(
					AtlasSaveStatus.Conflict,
					$"'{conflict}' changed outside Material Lab. Reload External or explicitly overwrite it.",
					Array.Empty<string>());
			}
		}

		List<SaveTarget> prepared = new();
		List<SaveTarget> replaced = new();
		try
		{
			foreach (SaveTarget target in targets)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(target.Path)!);
				target.TempPath = target.Path + $".material-lab-{Guid.NewGuid():N}.tmp";
				target.BackupPath = target.Path + $".material-lab-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.bak";
				EncodePng(target.Bitmap, target.TempPath);
				ValidatePng(target.Bitmap, target.TempPath);
				if (File.Exists(target.Path))
					File.Copy(target.Path, target.BackupPath, overwrite: false);
				prepared.Add(target);
			}

			// Recheck after all encoding and backup work, immediately before replacement.
			if (!overwriteConflicts)
			{
				string conflict = targets.Select(static target => target.Path).FirstOrDefault(HasConflict);
				if (conflict != null)
				{
					foreach (SaveTarget target in prepared)
						DeleteIfExists(target.BackupPath);
					return new AtlasSaveResult(AtlasSaveStatus.Conflict,
						$"'{conflict}' changed while the save was prepared. Nothing was replaced.", Array.Empty<string>());
				}
			}

			foreach (SaveTarget target in targets)
			{
				File.Move(target.TempPath, target.Path, overwrite: true);
				target.TempPath = null;
				replaced.Add(target);
			}

			foreach (SaveTarget target in targets)
				expectedHashes[target.Path] = HashFile(target.Path);
			foreach (AtlasSaveDocument document in documents)
			{
				string savedPath = GetTargets(document.Document.RelativePath).First();
				document.Document.AcceptSavedBitmap(document.Bitmap, savedPath);
			}
			foreach (SaveTarget target in prepared)
				DeleteIfExists(target.BackupPath);
			return new AtlasSaveResult(AtlasSaveStatus.Saved,
				$"Saved {targets.Count} atlas file{(targets.Count == 1 ? string.Empty : "s")}.", Array.Empty<string>());
		}
		catch (Exception saveError)
		{
			List<string> rollbackFailures = new();
			foreach (SaveTarget target in replaced.AsEnumerable().Reverse())
			{
				try
				{
					if (File.Exists(target.BackupPath))
						File.Copy(target.BackupPath, target.Path, overwrite: true);
					else
						File.Delete(target.Path);
				}
				catch (Exception rollbackError)
				{
					rollbackFailures.Add($"{target.Path}: {rollbackError.Message}");
				}
			}

			IReadOnlyList<string> recovery = prepared
				.Select(static target => target.BackupPath)
				.Where(File.Exists)
				.ToArray();
			if (rollbackFailures.Count > 0)
				return new AtlasSaveResult(AtlasSaveStatus.RollbackFailed,
					$"Save failed ({saveError.Message}) and rollback was incomplete: {string.Join("; ", rollbackFailures)}",
					recovery);

			foreach (string backup in recovery)
				DeleteIfExists(backup);
			return new AtlasSaveResult(AtlasSaveStatus.Failed,
				$"Save failed and all replaced files were restored: {saveError.Message}", Array.Empty<string>());
		}
		finally
		{
			foreach (SaveTarget target in prepared)
				DeleteIfExists(target.TempPath);
			foreach (AtlasSaveDocument document in documents)
				document.Dispose();
		}
	}

	private List<SaveTarget> BuildTargets(IReadOnlyList<AtlasSaveDocument> documents)
	{
		Dictionary<string, SaveTarget> unique = new(StringComparer.OrdinalIgnoreCase);
		foreach (AtlasSaveDocument document in documents)
		foreach (string path in GetTargets(document.Document.RelativePath))
		{
			string canonical = Path.GetFullPath(path);
			if (unique.TryGetValue(canonical, out SaveTarget existing))
			{
				if (!BitmapsEqual(existing.Bitmap, document.Bitmap))
					throw new InvalidOperationException($"Two atlas documents produced different content for '{canonical}'.");
				continue;
			}
			unique.Add(canonical, new SaveTarget(canonical, document.Bitmap));
		}
		return unique.Values.ToList();
	}

	private IEnumerable<string> GetTargets(string relativePath)
	{
		if (paths.SourceRoot != null)
			yield return Path.GetFullPath(Path.Combine(paths.SourceRoot, relativePath));
		string runtime = Path.GetFullPath(Path.Combine(paths.RuntimeRoot, relativePath));
		if (paths.SourceRoot == null || !string.Equals(
			runtime,
			Path.GetFullPath(Path.Combine(paths.SourceRoot, relativePath)),
			StringComparison.OrdinalIgnoreCase))
			yield return runtime;
	}

	private bool HasConflict(string path)
	{
		byte[] current = HashFile(path);
		return !expectedHashes.TryGetValue(path, out byte[] expected) || !current.AsSpan().SequenceEqual(expected);
	}

	private static byte[] HashFile(string path) =>
		File.Exists(path) ? SHA256.HashData(File.ReadAllBytes(path)) : Array.Empty<byte>();

	private static void EncodePng(Bitmap bitmap, string path)
	{
		using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		bitmap.Save(stream, ImageFormat.Png);
		stream.Flush(flushToDisk: true);
	}

	private static void ValidatePng(Bitmap expected, string path)
	{
		using Bitmap decoded = new(path);
		if (decoded.Width != expected.Width || decoded.Height != expected.Height
			|| !AtlasBitmapCodec.Read(decoded).AsSpan().SequenceEqual(AtlasBitmapCodec.Read(expected)))
			throw new InvalidDataException($"Temporary PNG validation failed for '{path}'.");
	}

	private static bool BitmapsEqual(Bitmap left, Bitmap right) =>
		left.Width == right.Width && left.Height == right.Height
		&& AtlasBitmapCodec.Read(left).AsSpan().SequenceEqual(AtlasBitmapCodec.Read(right));

	private static void DeleteIfExists(string path)
	{
		if (!string.IsNullOrEmpty(path) && File.Exists(path))
			File.Delete(path);
	}

	private sealed class SaveTarget
	{
		internal SaveTarget(string path, Bitmap bitmap)
		{
			Path = path;
			Bitmap = bitmap;
		}

		internal string Path { get; }
		internal Bitmap Bitmap { get; }
		internal string TempPath { get; set; }
		internal string BackupPath { get; set; }
	}
}
#endif

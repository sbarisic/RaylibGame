using FishUI;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;

namespace Voxelgine.GUI;

internal static class GameFishUIDiagnosticsPolicy
{
	internal const int HistoryEventLimit = 20_000;
	internal static readonly TimeSpan HistoryDuration = TimeSpan.FromSeconds(10);

	internal static void Apply(global::FishUI.FishUI context, RuntimePaths runtimePaths,
		IFishLogging logging)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(runtimePaths);
		ArgumentNullException.ThrowIfNull(logging);

		FishUIDiagnosticsSession diagnostics = context.Diagnostics;
		diagnostics.Enabled = true;
		diagnostics.HotkeyEnabled = true;
		diagnostics.RollingEventHistoryEnabled = true;
		diagnostics.RollingEventHistoryDuration = HistoryDuration;
		diagnostics.MaximumRollingHistoryEvents = HistoryEventLimit;
		diagnostics.MaximumCaptureEvents = HistoryEventLimit;
		diagnostics.MaximumPendingArtifactJobs = 2;
		diagnostics.MaximumDeferredCaptureRequests = 16;
		diagnostics.PrivacyPolicy.RedactText = true;
		diagnostics.PrivacyPolicy.RedactValues = false;
		diagnostics.PrivacyPolicy.AllowFramebufferCapture = true;
		diagnostics.ResetEventRecorder();

		string exportRoot = Path.Combine(runtimePaths.Root, "diagnostics", "fishui");
		diagnostics.AutoExportAsync = (snapshot, _) => Task.Run(() =>
		{
			string path = SaveUnique(snapshot, exportRoot);
			logging.Log(GameLogLevel.Info, "FishUI", $"Diagnostic snapshot exported to '{path}'.");
		});
		diagnostics.CaptureCompleted += (_, args) => logging.Log(
			GameLogLevel.Debug,
			"FishUI",
			$"Diagnostic capture {args.Snapshot.CaptureId}/{args.Snapshot.RequestId} completed with status {args.Snapshot.CaptureStatus}.");
		diagnostics.ExportFailed += (_, args) => logging.Log(
			GameLogLevel.Error,
			"FishUI",
			$"Diagnostic snapshot export failed for '{args.Snapshot.DefaultExportName}'.",
			args.Exception);
	}

	private static string SaveUnique(FishUIDebugSnapshot snapshot, string exportRoot)
	{
		Directory.CreateDirectory(exportRoot);
		for (int suffix = 0; ; suffix++)
		{
			string name = suffix == 0
				? snapshot.DefaultExportName
				: $"{snapshot.DefaultExportName}-{suffix:D2}";
			string path = Path.Combine(exportRoot, name);
			try
			{
				snapshot.SaveDirectory(path);
				return path;
			}
			catch (IOException) when (Directory.Exists(path) || File.Exists(path))
			{
				// A different context won the name. Try the next collision suffix.
			}
		}
	}
}

namespace Voxelgine.Engine;

public enum ApplicationKind
{
	Client,
	HostedServer,
	DedicatedServer,
	Test,
}

/// <summary>
/// Writable process data. Immutable game content remains below
/// <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public sealed record RuntimePaths(
	string Root,
	string ConfigurationFile,
	string WorldDirectory,
	string PlayerDirectory,
	string LogDirectory)
{
	public void CreateDirectories()
	{
		Directory.CreateDirectory(Root);
		Directory.CreateDirectory(WorldDirectory);
		Directory.CreateDirectory(PlayerDirectory);
		Directory.CreateDirectory(LogDirectory);
	}
}

public static class RuntimePathResolver
{
	/// <summary>
	/// Resolves all mutable process paths. <paramref name="dataRootOverride"/>
	/// may be null or blank to use the platform default.
	/// </summary>
	public static RuntimePaths ResolveRuntimePaths(
		ApplicationKind application,
		string dataRootOverride,
		string workingDirectory)
	{
		return ResolveRuntimePaths(application, dataRootOverride, workingDirectory, null);
	}

	/// <summary>
	/// Resolves all mutable process paths and reports a final-fallback warning.
	/// Both <paramref name="dataRootOverride"/> and <paramref name="warning"/>
	/// may be null.
	/// </summary>
	public static RuntimePaths ResolveRuntimePaths(
		ApplicationKind application,
		string dataRootOverride,
		string workingDirectory,
		Action<string> warning)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

		string root;
		if (!string.IsNullOrWhiteSpace(dataRootOverride))
		{
			root = dataRootOverride;
		}
		else if (application == ApplicationKind.Test)
		{
			root = Path.Combine(Path.GetTempPath(), "AuroraFallsTests", Guid.NewGuid().ToString("N"));
		}
		else
		{
			string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (string.IsNullOrWhiteSpace(localData))
			{
				string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				if (!string.IsNullOrWhiteSpace(userProfile))
				{
					localData = Path.Combine(userProfile, ".local", "share");
				}
			}

			if (string.IsNullOrWhiteSpace(localData))
			{
				localData = Path.Combine(workingDirectory, ".runtime");
				warning?.Invoke($"No per-user application-data directory is available; using '{localData}'.");
			}

			root = application switch
			{
				ApplicationKind.DedicatedServer => Path.Combine(localData, "AuroraFallsServer"),
				ApplicationKind.HostedServer => Path.Combine(localData, "AuroraFalls", "hosted-server"),
				_ => Path.Combine(localData, "AuroraFalls"),
			};
		}

		root = Path.GetFullPath(root, workingDirectory);
		return new RuntimePaths(
			root,
			Path.Combine(root, "config.json"),
			Path.Combine(root, "worlds"),
			Path.Combine(root, "players"),
			Path.Combine(root, "logs"));
	}
}

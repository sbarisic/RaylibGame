using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voxelgine.WorldGeneration;

/// <summary>System.Text.Json storage for one complete CeramicFish definition per file.</summary>
public sealed class CeramicFishJsonStorage : ICeramicFishJsonStorage
{
	private static readonly JsonSerializerOptions Options = CreateOptions();

	public async ValueTask<CeramicFishDefinition> LoadAsync(
		string path,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		string fullPath = Path.GetFullPath(path);
		await using FileStream input = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
			bufferSize: 16 * 1024, useAsync: true);
		CeramicFishDefinition definition;
		try
		{
			definition = await JsonSerializer.DeserializeAsync<CeramicFishDefinition>(
				input, Options, cancellationToken).ConfigureAwait(false)
				?? throw DefinitionError("definition-json-empty",
					"The CeramicFish JSON document is empty.");
		}
		catch (JsonException exception)
		{
			throw new CeramicDefinitionException("The CeramicFish JSON document is malformed.",
				[new("definition-json-malformed", exception.Message, exception.Path)], exception);
		}
		ValidateStructure(definition);
		return definition;
	}

	public async ValueTask SaveAsync(
		string path,
		CeramicFishDefinition definition,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(definition);
		ValidateStructure(definition);
		string fullPath = Path.GetFullPath(path);
		string? directory = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrEmpty(directory))
			throw DefinitionError("definition-path", "The CeramicFish JSON path has no parent directory.");
		Directory.CreateDirectory(directory);
		string temporary = Path.Combine(directory,
			$".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
		try
		{
			await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write,
				FileShare.None, bufferSize: 16 * 1024, useAsync: true))
			{
				await JsonSerializer.SerializeAsync(output, definition, Options, cancellationToken)
					.ConfigureAwait(false);
				await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
			_ = await LoadAsync(temporary, cancellationToken).ConfigureAwait(false);
			File.Move(temporary, fullPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	private static void ValidateStructure(CeramicFishDefinition definition)
	{
		CeramicValidationResult result = CeramicFishValidation.ValidateDefinition(definition);
		if (!result.IsValid)
			throw new CeramicDefinitionException("The CeramicFish definition failed structural validation.",
				result.Errors);
	}

	private static CeramicDefinitionException DefinitionError(string code, string message) =>
		new(message, [new(code, message, "$")]);

	private static JsonSerializerOptions CreateOptions()
	{
		JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
		{
			WriteIndented = true,
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		};
		options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
		return options;
	}
}

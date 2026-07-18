#if WINDOWS
using FishGfx.Graphics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Models;

namespace Voxelgine.FishGfxClient.Viewmodel;

/// <summary>
/// Draws the first-person arm and active JSON weapon directly into FishGfx's
/// depth-cleared viewmodel pass. GPU ownership remains with <see cref="Assets.GameAssetStore"/>.
/// </summary>
public sealed class FishGfxViewModelRenderer : IDisposable
{
	private static readonly ConditionalWeakTable<IFishGfxGameWindow, SharedModels> ModelsByWindow = new();
	private readonly SharedModels models;
	private readonly Action<string> log;
	private bool disposed;

	public FishGfxViewModelRenderer(IFishGfxGameWindow window, Action<string> log = null)
	{
		ArgumentNullException.ThrowIfNull(window);
		this.log = log ?? (_ => { });
		models = ModelsByWindow.GetValue(window, CreateSharedModels);
	}

	public void Render(RenderPass pass, Player player, ViewModel viewModel)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(pass);
		ArgumentNullException.ThrowIfNull(player);
		ArgumentNullException.ThrowIfNull(viewModel);

		if (!viewModel.IsActive)
		{
			return;
		}

		ViewModelRenderPose pose = viewModel.GetRenderPose(player);
		models.Arm.Draw(pass, pose.Position, Vector3.One, pose.Rotation);

		ModelAttachment attachment = pose.WeaponAsset switch
		{
			ViewModelAssetKind.Gun => models.Gun,
			ViewModelAssetKind.Hammer => models.Hammer,
			_ => null,
		};
		if (attachment is null)
		{
			return;
		}

		Vector3 gripWorldOffset = Vector3.Transform(
			attachment.GripOffset,
			pose.Rotation
		);
		Vector3 weaponPosition = pose.Position + gripWorldOffset;
		attachment.Model.Draw(pass, weaponPosition, Vector3.One, pose.Rotation);

		Vector3 muzzleLocal = attachment.GripOffset + attachment.ProjectileCenter;
		viewModel.SetMuzzlePoint(
			pose.Position + Vector3.Transform(muzzleLocal, pose.Rotation)
		);
	}

	public void Dispose()
	{
		// Shared resources are disposed once by GameAssetStore with the RenderWindow.
		disposed = true;
	}

	private SharedModels CreateSharedModels(IFishGfxGameWindow window)
	{
		try
		{
			const string armModel = "data/models/viewmodel_arm/viewmodel_arm.json";
			const string gunModel = "data/models/gun/gun.json";
			const string hammerModel = "data/models/hammer/hammer.json";

			FishGfxJsonModel arm = Load(
				window,
				"viewmodel.arm",
				armModel,
				"data/models/viewmodel_arm/viewmodel_arm_tex.png"
			);
			Vector3 handCenter = ReadElementCenter(armModel, "hand");

			ModelAttachment gun = LoadAttachment(
				window,
				"viewmodel.gun",
				gunModel,
				"data/models/gun/gun_tex.png",
				handCenter
			);
			ModelAttachment hammer = LoadAttachment(
				window,
				"viewmodel.hammer",
				hammerModel,
				"data/models/hammer/hammer_tex.png",
				handCenter
			);

			return new SharedModels(arm, gun, hammer);
		}
		catch (Exception exception)
		{
			log($"ViewModel: failed to load FishGfx JSON assets: {exception.Message}");
			throw;
		}
	}

	private static ModelAttachment LoadAttachment(
		IFishGfxGameWindow window,
		string id,
		string modelPath,
		string texturePath,
		Vector3 handCenter
	)
	{
		FishGfxJsonModel model = Load(window, id, modelPath, texturePath);
		Vector3 gripCenter = ReadElementCenter(modelPath, "grip");
		Vector3 projectileCenter = ReadElementCenter(modelPath, "projectile");
		return new ModelAttachment(model, handCenter - gripCenter, projectileCenter);
	}

	private static FishGfxJsonModel Load(
		IFishGfxGameWindow window,
		string id,
		string modelPath,
		string texturePath
	)
	{
		(int width, int height) = ReadTextureSize(modelPath);
		return new FishGfxJsonModel(
			window,
			id,
			modelPath,
			texturePath,
			width,
			height
		);
	}

	private static (int Width, int Height) ReadTextureSize(string modelPath)
	{
		using JsonDocument document = OpenModel(modelPath);
		JsonElement size = document.RootElement.GetProperty("texture_size");
		return (size[0].GetInt32(), size[1].GetInt32());
	}

	private static Vector3 ReadElementCenter(string modelPath, string elementName)
	{
		using JsonDocument document = OpenModel(modelPath);
		foreach (JsonElement element in document.RootElement.GetProperty("elements").EnumerateArray())
		{
			if (!string.Equals(
				element.GetProperty("name").GetString(),
				elementName,
				StringComparison.OrdinalIgnoreCase
			))
			{
				continue;
			}

			Vector3 from = ReadVector(element.GetProperty("from"));
			Vector3 to = ReadVector(element.GetProperty("to"));
			return (from + to) / 32 + new Vector3(-0.5f, 0, -0.5f);
		}

		throw new FormatException($"Model '{modelPath}' has no '{elementName}' element.");
	}

	private static JsonDocument OpenModel(string modelPath)
	{
		string fullPath = Path.IsPathRooted(modelPath)
			? Path.GetFullPath(modelPath)
			: Path.GetFullPath(modelPath, AppContext.BaseDirectory);
		return JsonDocument.Parse(File.ReadAllText(fullPath));
	}

	private static Vector3 ReadVector(JsonElement value)
	{
		return new Vector3(
			value[0].GetSingle(),
			value[1].GetSingle(),
			value[2].GetSingle()
		);
	}

	private sealed record ModelAttachment(
		FishGfxJsonModel Model,
		Vector3 GripOffset,
		Vector3 ProjectileCenter
	);

	private sealed record SharedModels(
		FishGfxJsonModel Arm,
		ModelAttachment Gun,
		ModelAttachment Hammer
	);
}
#endif

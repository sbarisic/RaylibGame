using System.Numerics;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine;

public enum ViewModelRotationMode
{
	Block,
	Tool,
	Gun,
	GunIronsight,
}

public enum ViewModelAssetKind
{
	None,
	Gun,
	Hammer,
}

public interface IViewModelAssetProvider
{
	ViewModelAssetKind ViewModelAsset { get; }
}

public readonly record struct ViewModelRenderPose(
	Vector3 Position,
	Quaternion Rotation,
	ViewModelAssetKind WeaponAsset
);

/// <summary>
/// Backend-neutral first-person viewmodel state. FishGfx owns all meshes and
/// textures; this class owns only pose, selection, and transient animation.
/// </summary>
public sealed class ViewModel : IDisposable
{
	private const float SubmergedPitchTarget = -25;
	private const float SubmergedLerpSpeed = 6;

	private readonly IFishEngineRunner engine;
	private readonly LerpVec3 offsetLerp;
	private readonly LerpVec3 kickbackLerp;
	private readonly LerpVec3 jiggleLerp;
	private readonly LerpFloat swingLerp;
	private ViewModelAssetKind selectedAsset;
	private ViewModelRotationMode rotationMode = ViewModelRotationMode.GunIronsight;
	private Vector3 desiredOffset;
	private Vector3 kickbackOffset;
	private Vector3 jiggleOffset;
	private Quaternion desiredRotation = Quaternion.Identity;
	private float swingAngle;
	private float submergedPitch;
	private bool isSwinging;
	private bool disposed;

	public ViewModel(IFishEngineRunner engine, bool useLegacyRenderer = false)
	{
		this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
		ILerpManager lerpManager = engine.DI.GetRequiredService<ILerpManager>();

		offsetLerp = new LerpVec3(lerpManager)
		{
			Easing = Easing.Linear,
			Loop = false,
		};
		offsetLerp.StartLerp(1, Vector3.Zero, Vector3.Zero);

		kickbackLerp = new LerpVec3(lerpManager)
		{
			Easing = Easing.EaseOutQuad,
			Loop = false,
		};
		kickbackLerp.StartLerp(0.01f, Vector3.Zero, Vector3.Zero);

		jiggleLerp = new LerpVec3(lerpManager)
		{
			Easing = Easing.EaseOutQuad,
			Loop = false,
		};
		jiggleLerp.StartLerp(0.01f, Vector3.Zero, Vector3.Zero);

		swingLerp = new LerpFloat(lerpManager)
		{
			Easing = Easing.EaseOutQuad,
			Loop = false,
		};
		swingLerp.StartLerp(0.01f, 0f, 0f);
		IsActive = true;
	}

	public Vector3 MuzzlePoint { get; private set; }

	public Vector3 ViewModelOffset { get; private set; }

	public Vector3 ViewModelPos { get; private set; }

	public Quaternion VMRot { get; private set; } = Quaternion.Identity;

	public bool IsActive { get; set; }

	public string GetDebugInfo()
	{
		string weapon = selectedAsset == ViewModelAssetKind.None
			? "Weapon: NO"
			: $"Weapon: {selectedAsset}";
		return $"Arm: YES | {weapon} | Mode: {rotationMode} | Renderer: FishGfx";
	}

	public void SetPresentationAsset(ViewModelAssetKind asset)
	{
		selectedAsset = asset;
	}

	internal void SetMuzzlePoint(Vector3 position)
	{
		MuzzlePoint = position;
	}

	public void SetRotationMode(ViewModelRotationMode mode)
	{
		rotationMode = mode;
	}

	public void ApplyKickback()
	{
		const float amount = 0.08f;
		const float duration = 0.12f;
		kickbackLerp.Easing = Easing.EaseOutQuad;
		kickbackLerp.StartLerp(
			duration * 0.3f,
			Vector3.Zero,
			new Vector3(0, 0, -amount)
		);
		kickbackLerp.OnComplete = _ =>
		{
			kickbackLerp.StartLerp(
				duration * 0.7f,
				new Vector3(0, 0, -amount),
				Vector3.Zero
			);
			kickbackLerp.OnComplete = null;
		};
	}

	public void ApplySwing()
	{
		if (isSwinging)
		{
			return;
		}

		isSwinging = true;
		const float amount = 60;
		const float duration = 0.35f;
		swingLerp.Easing = Easing.EaseOutCubic;
		swingLerp.StartLerp(duration * 0.4f, 0f, amount);
		swingLerp.OnComplete = _ =>
		{
			swingLerp.Easing = Easing.EaseOutQuad;
			swingLerp.StartLerp(duration * 0.6f, amount, 0f);
			swingLerp.OnComplete = _ =>
			{
				isSwinging = false;
				swingLerp.OnComplete = null;
			};
		};
	}

	public void ApplyJiggle()
	{
		const float duration = 0.18f;
		Vector3 reach = new(0, -0.03f, 0.04f);
		jiggleLerp.Easing = Easing.EaseOutQuad;
		jiggleLerp.StartLerp(duration * 0.35f, Vector3.Zero, reach);
		jiggleLerp.OnComplete = _ =>
		{
			jiggleLerp.StartLerp(duration * 0.65f, reach, Vector3.Zero);
			jiggleLerp.OnComplete = null;
		};
	}

	public void Update(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);
		ObjectDisposedException.ThrowIf(disposed, this);

		Vector3 forward = player.GetForward();
		Vector3 right = -player.GetLeft();
		Vector3 up = player.GetUp();
		Vector3 nextOffset = selectedAsset == ViewModelAssetKind.None
			? forward * 0.3f + right * 0.5f - up * 0.7f
			: rotationMode switch
			{
				ViewModelRotationMode.Block => forward * 0.7f + right * 0.4f - up * 0.6f,
				ViewModelRotationMode.Tool or ViewModelRotationMode.Gun =>
					forward * 0.7f + right * 0.6f - up * 0.6f,
				ViewModelRotationMode.GunIronsight =>
					forward * 0.55f + right * 0.045f - up * 0.48f,
				_ => throw new ArgumentOutOfRangeException(),
			};

		if (desiredOffset != nextOffset)
		{
			offsetLerp.StartLerp(0.2f, ViewModelOffset, nextOffset);
			desiredOffset = nextOffset;
		}

		ViewModelOffset = offsetLerp.GetVec3();
		kickbackOffset = kickbackLerp.GetVec3();
		jiggleOffset = jiggleLerp.GetVec3();
		swingAngle = swingLerp.GetFloat();

		bool isSubmerged = engine.AsClient().MultiplayerGameState?.Map?.IsWaterAt(player.Position) ?? false;
		float pitchTarget = isSubmerged ? SubmergedPitchTarget : 0;
		submergedPitch += (pitchTarget - submergedPitch)
			* MathF.Min(1, SubmergedLerpSpeed * 0.015f);
		ViewModelPos = player.Cam.Position + ViewModelOffset;

		Vector3 cameraRadians = player.GetCamAngle() * (MathF.PI / 180);
		Quaternion modelFlip = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
		Quaternion cameraRotation = Quaternion.CreateFromYawPitchRoll(
			cameraRadians.X,
			cameraRadians.Y,
			0
		);
		Quaternion modeAdjustment = selectedAsset == ViewModelAssetKind.None
			? Quaternion.Identity
			: rotationMode switch
			{
				ViewModelRotationMode.Block => Quaternion.CreateFromYawPitchRoll(
					Utils.ToRad(-22),
					Utils.ToRad(35),
					0
				),
				ViewModelRotationMode.Tool or ViewModelRotationMode.Gun =>
					Quaternion.CreateFromYawPitchRoll(Utils.ToRad(5), 0, 0),
				ViewModelRotationMode.GunIronsight =>
					Quaternion.CreateFromYawPitchRoll(0, Utils.ToRad(2), 0),
				_ => throw new ArgumentOutOfRangeException(),
			};
		Quaternion submergedRotation = MathF.Abs(submergedPitch) > 0.01f
			? Quaternion.CreateFromAxisAngle(Vector3.UnitX, Utils.ToRad(submergedPitch))
			: Quaternion.Identity;
		desiredRotation = Quaternion.Normalize(
			cameraRotation * submergedRotation * modeAdjustment * modelFlip
		);
		VMRot = Quaternion.Slerp(VMRot, desiredRotation, 0.2f);
	}

	public ViewModelRenderPose GetRenderPose(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);
		ObjectDisposedException.ThrowIf(disposed, this);

		Vector3 forward = player.GetForward();
		Vector3 right = -player.GetLeft();
		Vector3 up = player.GetUp();
		Vector3 position = player.RenderCam.Position
			+ ViewModelOffset
			+ forward * kickbackOffset.Z
			+ forward * jiggleOffset.Z
			+ up * jiggleOffset.Y;
		Quaternion rotation = VMRot;
		if (swingAngle != 0)
		{
			rotation = Quaternion.CreateFromAxisAngle(right, Utils.ToRad(swingAngle))
				* rotation;
		}

		return new ViewModelRenderPose(position, rotation, selectedAsset);
	}

	public void Dispose()
	{
		disposed = true;
	}
}

using System.Diagnostics;
using System.Numerics;
using Voxelgine.Engine.Audio;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine;

/// <summary>
/// Backend-neutral authoritative player state, movement, and wire serialization.
/// Client input, UI, and GPU resources are owned by the derived client player.
/// </summary>
public unsafe partial class Player : IDisposable
{
	public const float PlayerHeight = 1.7f;
	public const float PlayerEyeOffset = 1.6f;
	public const float PlayerRadius = 0.4f;

	private readonly Stopwatch LegTimer = Stopwatch.StartNew();
	private long LastWalkSound;
	private long LastJumpSound;
	private long LastCrashSound;
	private long LastSwimSound;
	private Vector3 PreviousPosition;
	private Vector3 Fwd;
	private Vector3 Left;
	private Vector3 Up;
	private int _selectedInventoryIndex;
	private readonly IGameAudioSink Snd;

	protected IFishEngineRunner Eng { get; }
	protected IFishLogging Logging { get; }

	public GameCameraState Cam = new(
		Vector3.Zero,
		Vector3.UnitZ,
		Vector3.UnitY,
		90f,
		CameraProjectionKind.Perspective
	);

	public GameCameraState RenderCam = new(
		Vector3.Zero,
		Vector3.UnitZ,
		Vector3.UnitY,
		90f,
		CameraProjectionKind.Perspective
	);

	public FPSCamera Camera { get; }
	public int PlayerId { get; }
	public float Health { get; set; } = 100f;
	public float MaxHealth { get; set; } = 100f;
	public bool IsDead => Health <= 0f;
	public bool NoClip;
	public bool FreezeFrustum;
	public bool CursorDisabled;
	public Vector3 Position;
	public AABB BBox { get; private set; }
	public Vector3 FeetPosition => Position - new Vector3(0f, PlayerEyeOffset, 0f);
	public Action<bool> OnMenuToggled;

	public Player(IFishEngineRunner engine, int playerId)
		: this(engine, playerId, NullGameAudioSink.Instance, 0.35f)
	{
	}

	protected Player(
		IFishEngineRunner engine,
		int playerId,
		IGameAudioSink audioSink,
		float mouseSensitivity
	)
	{
		Eng = engine ?? throw new ArgumentNullException(nameof(engine));
		Logging = engine.DI.GetRequiredService<IFishLogging>();
		PlayerId = playerId;
		Snd = audioSink ?? NullGameAudioSink.Instance;
		Camera = new FPSCamera(mouseSensitivity);
		SetPosition(Vector3.Zero);
	}

	public void TakeDamage(float amount)
	{
		if (!IsDead)
			Health = MathF.Max(0f, Health - amount);
	}

	public void ResetHealth()
	{
		Health = MaxHealth;
	}

	public void SetPosition(int x, int y, int z)
	{
		SetPosition(new Vector3(x, y, z));
	}

	public void SetPosition(Vector3 position)
	{
		if (float.IsNaN(position.X) || float.IsNaN(position.Y) || float.IsNaN(position.Z))
			return;

		PreviousPosition = Position;
		Position = position;
		Camera.Position = position;
		UpdateBoundingBox();
	}

	public Vector3 GetPreviousPosition() => PreviousPosition;
	public Vector3 GetForward() => Fwd;
	public Vector3 GetLeft() => Left;
	public Vector3 GetUp() => Up;

	public void SetCamAngle(Vector3 cameraAngle)
	{
		Camera.CamAngle = cameraAngle;
	}

	public Vector3 GetCamAngle() => Camera.CamAngle;

	public void UpdateDirectionVectors()
	{
		Fwd = Camera.GetForward();
		Left = Camera.GetLeft();
		Up = Camera.GetUp();
	}

	public virtual int GetSelectedInventoryIndex() => _selectedInventoryIndex;

	public virtual void SetSelectedInventoryIndex(int index)
	{
		_selectedInventoryIndex = index;
	}

	public void PlaySound(string cueId, Vector3 soundPosition)
	{
		Snd.Emit(new GameAudioEvent(cueId, soundPosition, GetVelocity(), PlayerId));
	}

	public virtual void Dispose()
	{
	}

	private void UpdateBoundingBox()
	{
		Vector3 feet = FeetPosition;
		Vector3 minimum = new(feet.X - PlayerRadius, feet.Y, feet.Z - PlayerRadius);
		Vector3 maximum = new(feet.X + PlayerRadius, feet.Y + PlayerHeight, feet.Z + PlayerRadius);
		BBox = AABB.FromMinMax(minimum, maximum);
	}
}

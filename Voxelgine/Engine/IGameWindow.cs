namespace Voxelgine.Engine;

/// <summary>
/// Client host boundary. Rendering resources remain owned by the concrete
/// FishGfx window and never leak into gameplay state.
/// </summary>
public interface IGameWindow : IDisposable
{
	InputMgr InMgr { get; }

	int Width { get; }

	int Height { get; }

	float AspectRatio { get; }

	void UpdateLockstep(float totalTime, float deltaTime);

	void Tick(float gameTime);

	void Render(float interpolationAlpha);

	void Close();

	bool IsOpen();

	void SetState(GameStateImpl state);
}

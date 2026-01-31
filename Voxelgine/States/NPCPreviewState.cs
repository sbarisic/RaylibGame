using FishUI.Controls;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Numerics;
using Voxelgine;
using Voxelgine.Engine;
using Voxelgine.GUI;

namespace RaylibGame.States
{
    /// <summary>
    /// Preview state for testing NPC models and animations.
    /// Displays the NPC in a 3D viewport with animation controls.
    /// </summary>
    public class NPCPreviewState : GameStateImpl
    {
        private FishUIManager _gui;
        private VEntNPC _previewNPC;
        private Camera3D _camera;
        private float _cameraAngle = 0f;
        private float _cameraDistance = 5f;
        private float _cameraHeight = 2f;
        private float _totalTime;

        // UI elements
        private Window _controlsWindow;
        private Label _animationLabel;
        private Label _timeLabel;

        public NPCPreviewState(GameWindow window) : base(window)
        {
            _gui = new FishUIManager(window);

            // Setup camera
            _camera = new Camera3D(
                new Vector3(0, 2, 5),    // Position
                new Vector3(0, 1, 0),    // Target (look at)
                Vector3.UnitY,            // Up
                45,                       // FOV
                CameraProjection.Perspective
            );

            // Create preview NPC
            _previewNPC = new VEntNPC();
            _previewNPC.SetSize(new Vector3(0.9f, 1.8f, 0.9f));
            _previewNPC.SetPosition(Vector3.Zero);
            _previewNPC.SetModel("npc/humanoid.json");

            CreateUI();
        }

        private void CreateUI()
        {
            var windowSize = new Vector2(280, 450);
            var windowPos = new Vector2(20, 20);

            _controlsWindow = new Window
            {
                Title = "NPC Animation Preview",
                Position = windowPos,
                Size = windowSize,
                IsResizable = false,
                ShowCloseButton = false
            };

            var stack = new StackLayout
            {
                Orientation = StackOrientation.Vertical,
                Spacing = 6,
                Position = new Vector2(10, 10),
                Size = new Vector2(windowSize.X - 40, windowSize.Y - 80),
                IsTransparent = true
            };

            // Animation info
            _animationLabel = new Label
            {
                Text = "Animation: idle",
                Size = new Vector2(220, 24)
            };
            stack.AddChild(_animationLabel);

            _timeLabel = new Label
            {
                Text = "Time: 0.00s",
                Size = new Vector2(220, 24)
            };
            stack.AddChild(_timeLabel);

            // Animation buttons section
            var animLabel = new Label
            {
                Text = "Animations:",
                Size = new Vector2(220, 24)
            };
            stack.AddChild(animLabel);

            var animator = _previewNPC.GetAnimator();

            // Create a button for each animation
            var btnIdle = new Button { Text = "Idle", Size = new Vector2(220, 35) };
            btnIdle.Clicked += (s, e) => animator?.Play("idle");
            stack.AddChild(btnIdle);

            var btnWalk = new Button { Text = "Walk", Size = new Vector2(220, 35) };
            btnWalk.Clicked += (s, e) => animator?.Play("walk");
            stack.AddChild(btnWalk);

            var btnAttack = new Button { Text = "Attack", Size = new Vector2(220, 35) };
            btnAttack.Clicked += (s, e) => animator?.Play("attack");
            stack.AddChild(btnAttack);

            var btnCrouch = new Button { Text = "Crouch", Size = new Vector2(220, 35) };
            btnCrouch.Clicked += (s, e) => animator?.Play("crouch");
            stack.AddChild(btnCrouch);

            // Control section
            var controlLabel = new Label
            {
                Text = "Controls:",
                Size = new Vector2(220, 24)
            };
            stack.AddChild(controlLabel);

            var btnPause = new Button { Text = "Pause/Resume", Size = new Vector2(220, 35) };
            btnPause.Clicked += (s, e) =>
            {
                if (animator?.State == NPCAnimationState.Playing)
                    animator.Pause();
                else
                    animator?.Resume();
            };
            stack.AddChild(btnPause);

            var btnReset = new Button { Text = "Reset Pose", Size = new Vector2(220, 35) };
            btnReset.Clicked += (s, e) => animator?.Stop();
            stack.AddChild(btnReset);

            // Back button
            var btnBack = new Button { Text = "Back to Main Menu", Size = new Vector2(220, 40) };
            btnBack.Clicked += (s, e) => Program.Window.SetState(Program.MainMenuState);
            stack.AddChild(btnBack);

            _controlsWindow.AddChild(stack);
            _gui.AddControl(_controlsWindow);
        }

        public override void SwapTo()
        {
            base.SwapTo();
            Raylib.EnableCursor();
        }

        public override void Tick(float GameTime)
        {
            float dt = Raylib.GetFrameTime();
            _totalTime += dt;

            // Camera orbit control with mouse drag
            if (Raylib.IsMouseButtonDown(MouseButton.Left) && !IsMouseOverUI())
            {
                Vector2 mouseDelta = Raylib.GetMouseDelta();
                _cameraAngle += mouseDelta.X * 0.01f;
                _cameraHeight += mouseDelta.Y * 0.03f;
                _cameraHeight = Math.Clamp(_cameraHeight, 0.5f, 5f);
            }

            // Zoom with scroll wheel
            float scroll = Raylib.GetMouseWheelMove();
            _cameraDistance -= scroll * 0.5f;
            _cameraDistance = Math.Clamp(_cameraDistance, 2f, 15f);

            // Update camera position
            _camera.Position = new Vector3(
                MathF.Sin(_cameraAngle) * _cameraDistance,
                _cameraHeight,
                MathF.Cos(_cameraAngle) * _cameraDistance
            );
            _camera.Target = new Vector3(0, 1, 0);

            // ESC to go back
            if (Window.InMgr.IsInputPressed(InputKey.Esc))
            {
                Program.Window.SetState(Program.MainMenuState);
            }
        }

        private bool IsMouseOverUI()
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            return mousePos.X < 300 && mousePos.Y < 470;
        }

        public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
        {
            // Update NPC animation
            var animator = _previewNPC.GetAnimator();
            animator?.Update(Dt);
        }

        public override void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo FInfo)
        {
            Raylib.ClearBackground(new Color(40, 44, 52));
            Raylib.BeginMode3D(_camera);

            // Draw ground grid
            Raylib.DrawGrid(10, 1.0f);

            // Draw coordinate axes
            Raylib.DrawLine3D(Vector3.Zero, new Vector3(2, 0, 0), Color.Red);
            Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 2, 0), Color.Green);
            Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 0, 2), Color.Blue);

            // Draw the NPC
            var model = _previewNPC.GetCustomModel();
            if (model != null)
            {
                model.Position = Vector3.Zero;
                model.LookDirection = Vector3.UnitZ;
                model.Draw();
            }

            Raylib.EndMode3D();
        }

        public override void Draw2D()
        {
            float dt = Raylib.GetFrameTime();
            _gui.Tick(dt, _totalTime);

            // Update labels
            var animator = _previewNPC.GetAnimator();
            if (animator != null)
            {
                _animationLabel.Text = $"Animation: {animator.CurrentAnimation ?? "none"} ({animator.State})";
                _timeLabel.Text = $"Time: {animator.CurrentTime:F2}s";
            }

            // Draw instructions
            Raylib.DrawText("Drag mouse to rotate | Scroll to zoom", Window.Width - 320, Window.Height - 30, 16, Color.LightGray);
            Raylib.DrawFPS(Window.Width - 100, 10);
        }
    }
}

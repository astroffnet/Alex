﻿using System;
using Alex.API.Input;
using Alex.API.Input.Listeners;
using Alex.Blocks;
using Alex.Entities;
using Alex.Worlds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NLog;
using BoundingBox = Microsoft.Xna.Framework.BoundingBox;

namespace Alex.Gamestates.Playing
{
    public class PlayerController
    {
	    private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(PlayerController));

		public PlayerIndex PlayerIndex { get; }
		public PlayerInputManager InputManager { get; }
        public bool IsFreeCam { get; set; }

        private GraphicsDevice Graphics { get; }
        private World World { get; }
        private Settings GameSettings { get; }

		private Player Player { get; }
		private InputManager GlobalInputManager { get; }

		public PlayerController(GraphicsDevice graphics, World world, Settings settings, InputManager inputManager, Player player, PlayerIndex playerIndex)
		{
			Player = player;
            Graphics = graphics;
            World = world;
            GameSettings = settings;
			PlayerIndex = playerIndex;

            IsFreeCam = true;

			GlobalInputManager = inputManager;
			InputManager = inputManager.GetOrAddPlayerManager(playerIndex);
		}

		private bool _inActive = true;
	    public bool CheckInput { get; set; } = false;
	    private bool IgnoreNextUpdate { get; set; } = false;
		private DateTime _lastForward = DateTime.UtcNow;
		public void Update(GameTime gameTime)
	    {
		    if (CheckInput)
		    {
			    var moveVector = Vector3.Zero;
				var now = DateTime.UtcNow;

				float speedFactor = 43.178f;
			    if (Player.IsSprinting && !Player.IsSneaking)
			    {
				    speedFactor *= 0.3f;
				    //speedFactor *= 0.2806f;
			    }
			    else if (Player.IsSneaking)
			    {
				    speedFactor *= 0.1f;
				}

			    if (Player.IsFlying)
			    {
				    speedFactor *= (float)Player.FlyingSpeed;
			    }
				else
			    {
				    speedFactor *= 0.15f;
			    }

				if (InputManager.IsPressed(InputCommand.ToggleCameraFree))
			    {
				    IsFreeCam = !IsFreeCam;
			    }

			    if (InputManager.IsDown(InputCommand.MoveForwards))
			    {
				    moveVector.Z += 1;
				    if (!Player.IsSprinting)
				    {
					    if (InputManager.IsBeginPress(InputCommand.MoveForwards) &&
					        now.Subtract(_lastForward).TotalMilliseconds <= 100)
					    {
						    Player.IsSprinting = true;
					    }
				    }

				    _lastForward = now;
			    }
			    else
			    {
				    if (Player.IsSprinting)
				    {
					    Player.IsSprinting = false;
				    }
			    }

			    if (InputManager.IsDown(InputCommand.MoveBackwards))
				    moveVector.Z -= 1;

			    if (InputManager.IsDown(InputCommand.MoveLeft))
				    moveVector.X += 1;

			    if (InputManager.IsDown(InputCommand.MoveRight))
				    moveVector.X -= 1;

				if (Player.IsFlying)
				{
					//speedFactor = (float)Player.FlyingSpeed;
					speedFactor *= 2.5f;

					if (InputManager.IsDown(InputCommand.MoveUp))
						moveVector.Y += 1;

					if (InputManager.IsDown(InputCommand.MoveDown))
					{
						moveVector.Y -= 1;
					    Player.IsSneaking = true;
				    }
				    else
					{ 
					    Player.IsSneaking = false;
				    }
			    }
			    else
			    {
				    if (InputManager.IsDown(InputCommand.MoveUp))
				    {
					    if (Math.Abs(Math.Floor(Player.KnownPosition.Y) - Player.KnownPosition.Y) < 0.001f)
						    Player.Velocity += new Vector3(0, 0.42f, 0);
				    }

				    if (InputManager.IsDown(InputCommand.MoveDown))
				    {
					    Player.IsSneaking = true;
				    }
				    else //if (_prevKeyState.IsKeyDown(KeyBinds.Down))
				    {
					    Player.IsSneaking = false;
				    }
			    }

			    if (moveVector != Vector3.Zero)
			    {
				    Player.Velocity += new Vector3(moveVector.X * speedFactor, moveVector.Y * speedFactor, moveVector.Z * speedFactor);
				}

			    if (IgnoreNextUpdate)
			    {
				    IgnoreNextUpdate = false;
				}
			    else
			    {
				    var e = this.GlobalInputManager.CursorInputListener.GetCursorPosition();

					var centerX = Graphics.Viewport.Width / 2;
				    var centerY = Graphics.Viewport.Height / 2;

				    if (e.X < 10 || e.X > Graphics.Viewport.Width - 10 ||
				        e.Y < 10 || e.Y > Graphics.Viewport.Height - 10)
				    {
					    Mouse.SetPosition(centerX, centerY);
					    IgnoreNextUpdate = true;
					}
				    else
				    {
					    var mouseDelta = this.GlobalInputManager.CursorInputListener.GetCursorPositionDelta();
					    var look = new Vector2((-mouseDelta.X), (-mouseDelta.Y))
					               * (float) (gameTime.ElapsedGameTime.TotalSeconds * 30);
					   
					    Player.KnownPosition.Yaw -= -look.X;
					    Player.KnownPosition.Pitch -= look.Y;
					    Player.KnownPosition.Yaw %= 360;
					    Player.KnownPosition.Pitch = MathHelper.Clamp(Player.KnownPosition.Pitch, -89.9f, 89.9f);

					    Player.KnownPosition.HeadYaw = Player.KnownPosition.Yaw;
				    }
			    }
			}
		    else if (!_inActive)
		    {
			    _inActive = true;
		    }
	    }
    }
}

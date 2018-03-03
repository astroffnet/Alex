﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Alex.API.Graphics;
using Alex.Gamestates;
using Alex.Rendering.Camera;
using Alex.ResourcePackLib.Json.Models;
using log4net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MiNET.Utils;

namespace Alex.Graphics.Models
{
	public class EntityModelRenderer
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof(EntityModelRenderer));
		private EntityModel Model { get; }
		private Texture2D Texture { get; }
		private BasicEffect Effect { get; set; }
		public EntityModelRenderer(EntityModel model, Texture2D texture)
		{
			Model = model;
			Texture = texture;

			Cache();
		}

		private VertexPositionNormalTexture[] Vertices { get; set; } = null;
		private VertexBuffer Buffer { get; set; }
		private void Cache()
		{
			float x = 0, y = 0, z = 0;
			List<VertexPositionNormalTexture> textures = new List<VertexPositionNormalTexture>();
			foreach (var bone in Model.Bones)
			{
				if (bone == null) continue;
				if (bone.NeverRender) continue;

				if (bone.Cubes != null)
				{
					foreach (var cube in bone.Cubes)
					{
						if (cube == null)
						{
							Log.Warn("Cube was null!");
							continue;
						}

						if (cube.Uv == null)
						{
							Log.Warn("Cube.UV was null!");
							continue;
						}

						if (cube.Origin == null)
						{
							Log.Warn("Cube.Origin was null!");
							continue;
						}

						if (cube.Size == null)
						{
							Log.Warn("Cube.Size was null!");
							continue;
						}

						var pixelSizeX = 1f / Texture.Width; //0.0625
						var pixelSizeY = 1f / Texture.Height;

						var x1 = cube.Uv.X * pixelSizeX;
						var x2 = (cube.Uv.X + cube.Size.X) * pixelSizeX; // + ((cube.Size.X) * pixelSizeX);
						var y1 = cube.Uv.Y * pixelSizeY;
						var y2 = (cube.Uv.Y + cube.Size.Y) * pixelSizeY; // + ((cube.Size.Y) * pixelSizeY);

						var size = new Vector3(cube.Size.X, cube.Size.Y, cube.Size.Z);
						var origin = new Vector3(cube.Origin.X, cube.Origin.Y, cube.Origin.Z);
						var built = BuildCube(size, origin, new Vector2(cube.Uv.X, cube.Uv.Y), new Vector2(Texture.Width, Texture.Height));

						textures.AddRange(built.Front);
						textures.AddRange(built.Back);
						textures.AddRange(built.Top);
						textures.AddRange(built.Bottom);
						textures.AddRange(built.Left);
						textures.AddRange(built.Right);
					}
				}
			}

			Vertices = textures.ToArray();
		}

		private Cube BuildCube(Vector3 size, Vector3 origin, Vector2 textureOrigin, Vector2 textureSize)
		{
			Cube cube = new Cube(size, textureSize);
			cube.BuildCube(textureOrigin);

			Mod(ref cube.Back, origin);
			Mod(ref cube.Front, origin);
			Mod(ref cube.Left, origin);
			Mod(ref cube.Right, origin);
			Mod(ref cube.Top, origin);
			Mod(ref cube.Bottom, origin);

			return cube;
		}

		private void Mod(ref VertexPositionNormalTexture[] data, Vector3 o)
		{
			for (int i = 0; i < data.Length; i++)
			{
				var pos = data[i].Position;

				pos = new Vector3(o.X + pos.X, o.Y + pos.Y, o.Z + pos.Z);
				//pos /= 16;
				data[i].Position = pos;
			}
		}

		private float _angle = 0f;
		public void Render(IRenderArgs args, Camera camera, Vector3 position)
		{
			if (Vertices == null || Vertices.Length == 0) return;

			if (Effect == null || Buffer == null)
			{
				//Effect = new AlphaTestEffect(args.GraphicsDevice);
				Effect = new BasicEffect(args.GraphicsDevice);

				Buffer = new VertexBuffer(args.GraphicsDevice,
					VertexPositionNormalTexture.VertexDeclaration, Vertices.Length, BufferUsage.WriteOnly);
			}

			Effect.World = Matrix.CreateScale(1f / 16f) * Matrix.CreateTranslation(position);
			//	    Effect.World = Matrix.CreateScale(1f / 16f) * Matrix.CreateRotationY(3 * _angle) * Matrix.CreateTranslation(position);

			Effect.View = camera.ViewMatrix;
			Effect.Projection = camera.ProjectionMatrix;

			Effect.TextureEnabled = true;
			Effect.Texture = Texture;


			foreach (var pass in Effect.CurrentTechnique.Passes)
			{
				pass.Apply();

				Buffer.SetData(Vertices);
				args.GraphicsDevice.SetVertexBuffer(Buffer);
			}

			args.GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, Vertices.Length);

			float dt = (float)args.GameTime.ElapsedGameTime.TotalSeconds;
			_angle += 0.5f * dt;
		}

		private class Cube
		{
			public Vector3 Size;

			private Vector2 TextureSize;

			public int Triangles = 12;

			public Cube(Vector3 size, Vector2 textureSize)
			{
				this.Size = size;
				this.TextureSize = textureSize; //new Vector2((size.X + size.Z) * 2, size.Y + size.Z);

				//front verts with position and texture stuff
				topLeftFront = new Vector3(0.0f, 1.0f, 0.0f) * Size;
				topLeftBack = new Vector3(0.0f, 1.0f, 1.0f) * Size;
				topRightFront = new Vector3(1.0f, 1.0f, 0.0f) * Size;
				topRightBack = new Vector3(1.0f, 1.0f, 1.0f) * Size;

				// Calculate the position of the vertices on the bottom face.
				btmLeftFront = new Vector3(0.0f, 0.0f, 0.0f) * Size;
				btmLeftBack = new Vector3(0.0f, 0.0f, 1.0f) * Size;
				btmRightFront = new Vector3(1.0f, 0.0f, 0.0f) * Size;
				btmRightBack = new Vector3(1.0f, 0.0f, 1.0f) * Size;
			}

			public VertexPositionNormalTexture[] Front, Back, Left, Right, Top, Bottom;

			private Vector3 topLeftFront,
				topLeftBack,
				topRightFront,
				topRightBack,
				btmLeftFront,
				btmLeftBack,
				btmRightFront,
				btmRightBack;

			public void BuildCube(Vector2 uv)
			{
				Front = GetFrontVertex(uv);
				Back = GetBackVertex(uv);
				Left = GetLeftVertex(uv);
				Right = GetRightVertex(uv);
				Top = GetTopVertex(uv);
				Bottom = GetBottomVertex(uv);
			}

			private VertexPositionNormalTexture[] GetLeftVertex(Vector2 uv)
			{
				Vector3 normal = new Vector3(-1.0f, 0.0f, 0.0f) * Size;

				var map = GetTextureMapping(uv + new Vector2(Size.Z + Size.X, Size.Z), Size.Z, Size.Y);

				// Add the vertices for the RIGHT face. 
				return new VertexPositionNormalTexture[]
				{
					new VertexPositionNormalTexture(topLeftFront, normal, map.TopLeft),
					new VertexPositionNormalTexture(btmLeftFront , normal, map.BotLeft),
					new VertexPositionNormalTexture(btmLeftBack, normal, map.BotRight),
					new VertexPositionNormalTexture(topLeftBack , normal, map.TopRight),
					new VertexPositionNormalTexture(topLeftFront , normal, map.TopLeft),
					new VertexPositionNormalTexture(btmLeftBack, normal, map.BotRight),
				};
			}

			private VertexPositionNormalTexture[] GetRightVertex(Vector2 uv)
			{
				Vector3 normal = new Vector3(1.0f, 0.0f, 0.0f) * Size;

				var map = GetTextureMapping(uv + new Vector2(0, Size.Z), Size.Z, Size.Y);

				// Add the vertices for the RIGHT face. 
				return new VertexPositionNormalTexture[]
				{
					new VertexPositionNormalTexture(topRightFront, normal, map.TopRight),
					new VertexPositionNormalTexture(btmRightBack , normal, map.BotLeft),
					new VertexPositionNormalTexture(btmRightFront, normal, map.BotRight),
					new VertexPositionNormalTexture(topRightBack , normal, map.TopLeft),
					new VertexPositionNormalTexture(btmRightBack , normal, map.BotLeft),
					new VertexPositionNormalTexture(topRightFront, normal, map.TopRight),
				};
			}

			private VertexPositionNormalTexture[] GetFrontVertex(Vector2 uv)
			{
				Vector3 normal = new Vector3(0.0f, 0.0f, 1.0f) * Size;

				var map = GetTextureMapping(uv + new Vector2(Size.Z, Size.Z), Size.X, Size.Y);

				// Add the vertices for the RIGHT face. 
				return new VertexPositionNormalTexture[]
				{
					new VertexPositionNormalTexture(topLeftFront , normal, map.TopLeft),
					new VertexPositionNormalTexture(topRightFront, normal, map.TopRight),
					new VertexPositionNormalTexture(btmLeftFront , normal, map.BotLeft),
					new VertexPositionNormalTexture(btmLeftFront , normal, map.BotLeft),
					new VertexPositionNormalTexture(topRightFront, normal, map.TopRight),
					new VertexPositionNormalTexture(btmRightFront, normal, map.BotRight),
				};
			}
			private VertexPositionNormalTexture[] GetBackVertex(Vector2 uv)
			{
				Vector3 normal = new Vector3(0.0f, 0.0f, -1.0f) * Size;

				var map = GetTextureMapping(uv + new Vector2(Size.Z + Size.Z + Size.X, Size.Z), Size.X, Size.Y);

				// Add the vertices for the RIGHT face. 
				return new VertexPositionNormalTexture[]
				{
					new VertexPositionNormalTexture(topLeftBack , normal, map.TopRight),
					new VertexPositionNormalTexture(btmLeftBack , normal, map.BotRight),
					new VertexPositionNormalTexture(topRightBack, normal, map.TopLeft),
					new VertexPositionNormalTexture(btmLeftBack , normal, map.BotRight),
					new VertexPositionNormalTexture(btmRightBack, normal, map.BotLeft),
					new VertexPositionNormalTexture(topRightBack, normal, map.TopLeft),
				};
			}

			private VertexPositionNormalTexture[] GetTopVertex(Vector2 uv)
			{
				Vector3 normal = new Vector3(0.0f, 1.0f, 0.0f) * Size;

				var map = GetTextureMapping(uv + new Vector2(Size.Z, 0), Size.X, Size.Z);

				// Add the vertices for the RIGHT face. 
				return new VertexPositionNormalTexture[]
				{
					new VertexPositionNormalTexture(topLeftFront , normal, map.BotLeft),
					new VertexPositionNormalTexture(topLeftBack  , normal, map.TopLeft),
					new VertexPositionNormalTexture(topRightBack , normal, map.TopRight),
					new VertexPositionNormalTexture(topLeftFront , normal, map.BotLeft),
					new VertexPositionNormalTexture(topRightBack , normal, map.TopRight),
					new VertexPositionNormalTexture(topRightFront, normal, map.BotRight),
				};
			}

			private VertexPositionNormalTexture[] GetBottomVertex(Vector2 uv)
			{
				Vector3 normal = new Vector3(0.0f, -1.0f, 0.0f) * Size;

				var map = GetTextureMapping(uv + new Vector2(Size.Z + Size.X, 0), Size.X, Size.Z);

				// Add the vertices for the RIGHT face. 
				return new VertexPositionNormalTexture[]
				{
					new VertexPositionNormalTexture(btmLeftFront , normal, map.TopLeft),
					new VertexPositionNormalTexture(btmRightBack , normal, map.BotRight),
					new VertexPositionNormalTexture(btmLeftBack  , normal, map.BotLeft),
					new VertexPositionNormalTexture(btmLeftFront , normal, map.TopLeft),
					new VertexPositionNormalTexture(btmRightFront, normal, map.TopRight),
					new VertexPositionNormalTexture(btmRightBack , normal, map.BotRight),
				};
			}



			private TextureMapping GetTextureMapping(Vector2 textureOffset, float regionWidth, float regionHeight)
			{
				return new TextureMapping(TextureSize, textureOffset, regionWidth, regionHeight);
			}

			private class TextureMapping
			{
				public Vector2 TopLeft { get; }
				public Vector2 TopRight { get; }
				public Vector2 BotLeft { get; }
				public Vector2 BotRight { get; }

				public TextureMapping(Vector2 textureSize, Vector2 textureOffset, float width, float height)
				{
					var pixelWidth = (1f / textureSize.X);
					var pixelHeight = (1f / textureSize.Y);

					var x1 = pixelWidth  * textureOffset.X;
					var x2 = pixelWidth  * (textureOffset.X + width);
					var y1 = pixelHeight * textureOffset.Y;
					var y2 = pixelHeight * (textureOffset.Y + height);

					TopLeft  = new Vector2(x1, y1);
					TopRight = new Vector2(x2, y1);
					BotLeft  = new Vector2(x1, y2);
					BotRight = new Vector2(x2, y2);
				}
			}
		}
	}
}

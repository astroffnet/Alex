﻿using System;
using System.Collections.Generic;
using System.Linq;
using Alex.API.Graphics;
using Alex.API.World;
using Alex.Blocks;
using Alex.Blocks.Properties;
using Alex.Utils;
using Microsoft.Xna.Framework;
using Alex.ResourcePackLib.Json;

namespace Alex.Graphics.Models
{
	public class LiquidBlockModel : BlockModel
	{
		private static PropertyInt LEVEL = new PropertyInt("level");
		private static PropertyBool WATERLOGGED = new PropertyBool("waterlogged");

		public bool IsLava = false;
		public bool IsWater => !IsLava;
		public bool IsFlowing = false;
		public int Level = 8;

		public LiquidBlockModel()
		{

		}

		public override VertexPositionNormalTextureColor[] GetVertices(IWorld world, Vector3 position, Block baseBlock)
		{
			List< VertexPositionNormalTextureColor> result = new List<VertexPositionNormalTextureColor>();
			int tl = 0, tr = 0, bl = 0, br = 0;

			Level = baseBlock.BlockState.GetTypedValue(LEVEL);

			string b1, b2;
			if (IsLava)
			{
				b1 = "minecraft:lava";
				b2 = "minecraft:lava";
			}
			else
			{
				b1 = "minecraft:water";
				b2 = "minecraft:water";
			}

			var bc = world.GetBlock(position + Vector3.Up);//.GetType();
			if ((!IsLava && bc.IsWater) || (IsLava && bc.Name == "minecraft:lava")) //.Name == b1 || bc.Name == b2)
			{
				tl = 8;
				tr = 8;
				bl = 8;
				br = 8;
			}
			else
			{
				tl = GetAverageLiquidLevels(world, position);
				tr = GetAverageLiquidLevels(world, position + Vector3.UnitX);
				bl = GetAverageLiquidLevels(world, position + Vector3.UnitZ);
				br = GetAverageLiquidLevels(world, position + new Vector3(1, 0, 1));
			}

			string texture = "";
			if (IsLava)
			{
				texture = "lava";
			}
			else
			{
				texture = "water";
			}
			texture = texture + "_flow";
			if (IsFlowing)
			{
			//	texture = texture + "_flow";
			}
			else
			{
			//	texture = texture + "_still";
			}

			//float frameX 
			UVMap map = GetTextureUVMap(Alex.Instance.Resources, texture, 0, 16, 0, 16);

			foreach (var f in Enum.GetValues(typeof(BlockFace)).Cast<BlockFace>())
			{
				Vector3 d = Vector3.Zero;
				switch (f)
				{
					case BlockFace.Up:
						d = Vector3.Up;
						break;
					case BlockFace.Down:
						d = Vector3.Down;
						break;
					case BlockFace.North:
						d = Vector3.Backward;
						break;
					case BlockFace.South:
						d = Vector3.Forward;
						break;
					case BlockFace.West:
						d = Vector3.Left;
						break;
					case BlockFace.East:
						d = Vector3.Right;
						break;
				}

				float height = 0;
				bool special = f == BlockFace.Up && (tl < 8 || tr < 8 || bl < 8 || br < 8);
				
				var b = (Block)world.GetBlock(position + d);
				LiquidBlockModel m = b.BlockModel as LiquidBlockModel;
				var secondSpecial = m != null && m.Level > Level;

				float s = 1f - Scale;
				var start = Vector3.One * s;
				var end = Vector3.One * Scale;

				if (special || (secondSpecial) || (!string.IsNullOrWhiteSpace(b.Name) && (!b.Name.Equals(b1) && !b.Name.Equals(b2))))
				{
					//if (b.BlockModel is LiquidBlockModel m && m.Level > Level && f != BlockFace.Up) continue;

					var vertices = GetFaceVertices(f, start, end, map);
					
					for (var index = 0; index < vertices.Length; index++)
					{
						var vert = vertices[index];

						if (vert.Position.Y > start.Y)
						{
							const float modifier = 2f;
							if (vert.Position.X == start.X && vert.Position.Z == start.Z)
							{
								height = (modifier * (tl));
							}
							else if (vert.Position.X != start.X && vert.Position.Z == start.Z)
							{
								height = (modifier * (tr));
							}
							else if (vert.Position.X == start.X && vert.Position.Z != start.Z)
							{
								height = (modifier * (bl));
							}
							else
							{
								height = (modifier * (br));
							}

							vert.Position.Y = height / 16.0f; //; + (position.Y);
						}

						vert.Position.Y += position.Y - s;
						vert.Position.X += position.X;
						vert.Position.Z += position.Z;

						vert.Color = LightingUtils.AdjustColor(vert.Color, f, GetLight(world, position), false);

						result.Add(vert);
					}
				}
			}

			return result.ToArray();
		}

		protected int GetAverageLiquidLevels(IWorld world, Vector3 position)
		{
			int level = 0;
			for (int xx = -1; xx <= 0; xx++)
			{
				for (int zz = -1; zz <= 0; zz++)
				{
					var b = (Block)world.GetBlock(position.X + xx, position.Y + 1, position.Z + zz);
					if ((b.BlockModel is LiquidBlockModel m && m.IsLava == IsLava))
					{
						return 8;
					}

					b = (Block)world.GetBlock(position.X + xx, position.Y, position.Z + zz);
					if ((b.BlockModel is LiquidBlockModel l && l.IsLava == IsLava))
					{
						var nl = 7 - (l.Level & 0x7);
						if (nl > level)
						{
							level = nl;
						}
					}
					else if (b != null && b.BlockState != null && b.BlockState.GetTypedValue(WATERLOGGED)) //Block is 'waterlogged'
					{
						level = 8;
					}
				}
			}

			return level;
		}


	}
}
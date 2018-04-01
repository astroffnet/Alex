﻿using System;
using Alex.API.Blocks.State;
using Alex.API.Utils;
using Alex.API.World;
using Alex.Blocks.State;
using Microsoft.Xna.Framework;

namespace Alex.Worlds.Generators
{
    public class FlatlandGenerator : IWorldGenerator
    {
	    private IBlockState Bedrock = BlockFactory.GetBlockState("minecraft:bedrock");
	    private IBlockState Dirt = BlockFactory.GetBlockState("minecraft:dirt");
	    private IBlockState Grass = BlockFactory.GetBlockState("minecraft:grass_block");

		public FlatlandGenerator()
	    {

	    }

	    public IChunkColumn GenerateChunkColumn(ChunkCoordinates chunkCoordinates)
	    {
		    ChunkColumn column = new ChunkColumn();
		    column.X = chunkCoordinates.X;
		    column.Z = chunkCoordinates.Z;

		    for (int x = 0; x < 16; x++)
		    {
			    for (int z = 0; z < 16; z++)
			    {
					column.SetBlockState(x, 0, z, Bedrock);
				    column.SetBlockState(x, 1, z, Dirt);
				    column.SetBlockState(x, 2, z, Dirt);
				    column.SetBlockState(x, 3, z, Grass);

				    column.SetSkyLight(x, 0, z, 15);
				    column.SetSkyLight(x, 1, z, 15);
				    column.SetSkyLight(x, 2, z, 15);
				    column.SetSkyLight(x, 3, z, 15);
				    column.SetSkyLight(x, 4, z, 15);
				}
		    }

		    return column;
	    }

	    public Vector3 GetSpawnPoint()
	    {
		    return new Vector3(0, 16, 0);
	    }

	    public void Initialize()
	    {
		    
	    }

	    public LevelInfo GetInfo()
	    {
		    return new LevelInfo();
	    }
    }
}
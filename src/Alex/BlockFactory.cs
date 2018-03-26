﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Alex.API.Blocks.Properties;
using Alex.API.Blocks.State;
using Alex.Blocks;
using Alex.Blocks.Properties;
using Alex.Blocks.State;
using Alex.Graphics.Models;
using Alex.ResourcePackLib;
using Alex.ResourcePackLib.Json;
using Alex.ResourcePackLib.Json.BlockStates;
using Alex.Worlds;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using BlockState = Alex.ResourcePackLib.Json.BlockStates.BlockState;

namespace Alex
{
	public static class BlockFactory
	{
		private static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger(typeof(BlockFactory));

		public static IReadOnlyDictionary<uint, IBlockState> AllBlockstates => new ReadOnlyDictionary<uint, IBlockState>(RegisteredBlockStates);
		public static IReadOnlyDictionary<string, IBlockState> AllBlockstatesByName => new ReadOnlyDictionary<string, IBlockState>(BlockStateByName);
		private static readonly Dictionary<uint, IBlockState> RegisteredBlockStates = new Dictionary<uint, IBlockState>();
		private static readonly Dictionary<string, IBlockState> BlockStateByName = new Dictionary<string, IBlockState>();

		//private static readonly Dictionary<uint, Block> RegisteredBlocks = new Dictionary<uint, Block>();
		private static readonly Dictionary<uint, BlockModel> ModelCache = new Dictionary<uint, BlockModel>();

		private static readonly Dictionary<string, BlockMeta> CachedBlockMeta = new Dictionary<string, BlockMeta>();

		private static ResourcePackLib.Json.Models.Blocks.BlockModel CubeModel { get; set; }
		public static readonly LiquidBlockModel StationairyWaterModel = new LiquidBlockModel()
		{
			IsFlowing = false,
			IsLava = false,
			Level = 8
		};

		public static readonly LiquidBlockModel FlowingWaterModel = new LiquidBlockModel()
		{
			IsFlowing = true,
			IsLava = false,
			Level = 8
		};

		public static readonly LiquidBlockModel StationairyLavaModel = new LiquidBlockModel()
		{
			IsFlowing = false,
			IsLava = true,
			Level = 8
		};

		public static readonly LiquidBlockModel FlowingLavaModel = new LiquidBlockModel()
		{
			IsFlowing = true,
			IsLava = true,
			Level = 8
		};

		internal static void Init()
		{
			JArray blockArray = JArray.Parse(Encoding.UTF8.GetString(Resources.blocks));
			Dictionary<string, JObject> blockMetaDictionary =
				JsonConvert.DeserializeObject<Dictionary<string, JObject>>(
					Encoding.UTF8.GetString(Resources.blockstates_without_models_pretty));
			foreach (var item in blockArray)
			{
				byte id = 0;
				bool transparent = false;
				string name = string.Empty;
				string displayName = string.Empty;

				foreach (dynamic entry in item)
				{
					if (entry.Name == "id")
					{
						id = entry.Value;
					}
					else if (entry.Name == "transparent")
					{
						transparent = entry.Value;
					}
					else if (entry.Name == "name")
					{
						name = entry.Value;
					}
					else if (entry.Name == "displayName")
					{
						displayName = entry.Value;
					}
				}

				if (id == 0 || string.IsNullOrWhiteSpace(name)) continue;

				BlockMeta meta = new BlockMeta
				{
					ID = id,
					Transparent = transparent,
					DisplayName = displayName,
					Name = name,
					Solid = true
				};

				JObject found = blockMetaDictionary
					.FirstOrDefault(x => x.Key.StartsWith($"minecraft:{name}", StringComparison.InvariantCultureIgnoreCase)).Value;
				if (found != null)
				{
					meta.AmbientOcclusionLightValue = found["ambientOcclusionLightValue"].Value<double>();
					meta.IsFullBlock = found["isFullBlock"].Value<bool>();
					meta.LightOpacity = found["lightOpacity"].Value<int>();
					meta.LightValue = found["lightValue"].Value<int>();
					meta.IsBlockNormalCube = found["isBlockNormalCube"].Value<bool>();
					meta.IsSideSolid = found["isSideSolid"].ToObject<Dictionary<string, bool>>();
					meta.IsFullCube = found["isFullCube"].Value<bool>();
				}
				CachedBlockMeta.TryAdd($"minecraft:{displayName.Replace(" ", "_").ToLowerInvariant()}", meta);
				CachedBlockMeta.TryAdd($"minecraft:{name}", meta);
			}
		}

		private static BlockModel GetOrCacheModel(ResourceManager resources, McResourcePack resourcePack, IBlockState state, uint id)
		{
			if (ModelCache.TryGetValue(id, out var r))
			{
				return r;
			}
			else
			{
				var result = ResolveModel(resources, resourcePack, state);
				if (result == null)
				{
					return null;
				}


				if (state.GetTypedValue(WaterLoggedProperty))
				{
					result = new MultiBlockModel(result, StationairyWaterModel);
				}

				if (!ModelCache.TryAdd(id, result))
				{
					Log.Warn($"Could not register model in cache! {state.Name} - {state.ID}");
				}

				return result;
			}
		}

		public partial class TableEntry
		{
			[JsonProperty("runtimeID")]
			public uint RuntimeId { get; set; }

			[JsonProperty("name")]
			public string Name { get; set; }

			[JsonProperty("id")]
			public long Id { get; set; }

			[JsonProperty("data")]
			public long Data { get; set; }

			public static TableEntry[] FromJson(string json) => JsonConvert.DeserializeObject<TableEntry[]>(json);
		}

		private static bool _builtin = false;
		private static void RegisterBuiltinBlocks()
		{
			if (_builtin)
				return;

			_builtin = true;

			//RegisteredBlockStates.Add(Block.GetBlockStateID(), StationairyWaterModel);
		}

		internal static int LoadResources(ResourceManager resources, McResourcePack resourcePack, bool replace,
			bool reportMissing = false)
		{
			if (resourcePack.TryGetBlockModel("cube_all", out ResourcePackLib.Json.Models.Blocks.BlockModel cube))
			{
				cube.Textures["all"] = "no_texture";
				CubeModel = cube;
			}

			RegisterBuiltinBlocks();

			return LoadModels(resources, resourcePack, replace, reportMissing);
		}

		private static PropertyBool WaterLoggedProperty = new PropertyBool("waterlogged");
		internal static bool GenerateClasses { get; set; } = false;
		private static int LoadModels(ResourceManager resources, McResourcePack resourcePack, bool replace,
			bool reportMissing)
		{
			StringBuilder factoryBuilder = new StringBuilder();

			TableEntry[] tablesEntries = TableEntry.FromJson(Resources.runtimeid_table);
			var data = BlockData.FromJson(Resources.NewBlocks);
			int importCounter = 0;

			uint c = 0;
			foreach (var entry in data)
			{
				Blocks.State.BlockState state = new Blocks.State.BlockState();
				state.Name = entry.Key;

				if (entry.Value.Properties != null)
				{
					foreach (var property in entry.Value.Properties)
					{
						state = (Blocks.State.BlockState)state.WithProperty(new DynamicStateProperty(property.Key, property.Value), property.Value.FirstOrDefault());
					}
				}

				foreach (var s in entry.Value.States)
				{
					var id = s.ID;

					Blocks.State.BlockState blockStateData = (Blocks.State.BlockState)state.Clone();
					blockStateData.Variants.Clear();
					
					blockStateData.Name = state.Name;
					blockStateData.ID = id;

					if (s.Properties != null)
					{
						foreach (var property in s.Properties)
						{
							blockStateData = (Blocks.State.BlockState)blockStateData.WithProperty(StateProperty.Parse(property.Key), property.Value);
						}
					}

					if (RegisteredBlockStates.TryGetValue(id, out IBlockState st))
					{
						Log.Warn($"Duplicate blockstate id (Existing: {st.Name}[{st.ToString()}] | New: {entry.Key}[{blockStateData.ToString()}]) ");
						continue;
					}

					{
						var cachedBlockModel = GetOrCacheModel(resources, resourcePack, blockStateData, id);
						if (cachedBlockModel == null)
						{
							if (reportMissing)
								Log.Warn($"Missing blockmodel for blockstate {entry.Key}[{blockStateData.ToString()}]");

							cachedBlockModel = new CachedResourcePackModel(resources, new BlockStateModel[]
							{
								new BlockStateModel()
								{
									Model = CubeModel,
									ModelName = "Unknown model",
								}
							});
						}

						string displayName = entry.Key;
						var block = GetBlockByName(entry.Key);
						if (block == null)
						{
							block = new UnknownBlock(id);
							displayName = $"(Not implemented) {displayName}";

							bool foundMeta = false;
							BlockMeta knownMeta = null;
							if (CachedBlockMeta.TryGetValue(entry.Key, out knownMeta))
							{
								foundMeta = true;
								block.Transparent = knownMeta.Transparent;
								block.LightValue = knownMeta.LightValue;
								block.AmbientOcclusionLightValue = knownMeta.AmbientOcclusionLightValue;
								block.LightOpacity = knownMeta.LightOpacity;
								block.IsBlockNormalCube = knownMeta.IsBlockNormalCube;
								block.IsFullCube = knownMeta.IsFullCube;
								block.IsFullBlock = knownMeta.IsFullBlock;
								block.Solid = knownMeta.Solid;
								block.Drag = knownMeta.FrictionFactor;
								block.IsReplacible = knownMeta.Replacible;
							}

							block.Name = entry.Key;

							if (s.Default && GenerateClasses)
							{
								string className = ToPascalCase(block.Name.Substring(10));

								factoryBuilder.AppendLine($"\t\t\telse if (blockName == \"{entry.Key.ToLowerInvariant()}\" || blockName == \"{className.ToLowerInvariant()}\") return new {className}();");

								SaveBlock(block, className, foundMeta);
								Log.Info($"Saved un-implemnted block to file ({displayName})!");
							}
						}

						if (block.IsSourceBlock && !(cachedBlockModel is MultiBlockModel) && !(cachedBlockModel is LiquidBlockModel))
						{
							if (block.IsWater)
							{
								cachedBlockModel = new MultiBlockModel(cachedBlockModel, StationairyWaterModel);
							}
							else
							{
								cachedBlockModel = new MultiBlockModel(cachedBlockModel, StationairyLavaModel);
							}

							block.Transparent = true;
						}

						if (blockStateData.GetTypedValue(WaterLoggedProperty))
						{
							block.Transparent = true;
						}

						//block.BlockStateID = id;
						block.Name = entry.Key;
						block.BlockModel = cachedBlockModel;

						block.BlockState = blockStateData;
						block.DisplayName = displayName;

						blockStateData.Block = block;
						blockStateData.Default = state;

						if (s.Default) //This is the default variant.
						{
							state.Block = block;
							state.Default = blockStateData;
							state.ID = id;
						}
						else
						{
							state.Variants.Add(blockStateData);
							if (!RegisteredBlockStates.TryAdd(id, blockStateData))
							{
								Log.Warn($"Failed to add blockstate (variant), key already exists! ({blockStateData.Name})");
							}
							else
							{
								importCounter++;
							}
						}
					}
				}

				if (!RegisteredBlockStates.TryAdd(state.ID, state))
				{
					Log.Warn($"Failed to register default blockstate! {state.Name} - {state.ID}");
				}

				if (!BlockStateByName.TryAdd(state.Name, state))
				{
					Log.Warn($"Failed to add blockstate, key already exists! ({state.Name})");
				}
				else
				{
					foreach (var bsVariant in state.Variants.Cast<Blocks.State.BlockState>())
					{
						bsVariant.Variants.AddRange(state.Variants.Where(x => !x.ID.Equals(bsVariant.ID)));
					}
				}
			}

			if (GenerateClasses)
			{
				File.WriteAllBytes("generated\\blockFactoryChanges.txt", Encoding.UTF8.GetBytes(factoryBuilder.ToString()));
			}

			return importCounter;
		}

		private static void SaveBlock(Block block, string className, bool hasMeta)
		{
			StringBuilder builder = new StringBuilder();
			builder.AppendLine("using Alex.Utils;");
			builder.AppendLine("using Alex.Worlds;");
			builder.AppendLine();
			builder.AppendLine("namespace Alex.Blocks");
			builder.AppendLine("{");

			builder.AppendLine($"\tpublic class {className} : Block");
			builder.AppendLine("\t{");
			builder.AppendLine($"\t\tpublic {className}() : base({block.BlockState.ID.ToString()})");
			builder.AppendLine("\t\t{");
			builder.AppendLine($"\t\t\tSolid = {block.Solid.ToString().ToLower()};");
			builder.AppendLine($"\t\t\tTransparent = {block.Transparent.ToString().ToLower()};");
			builder.AppendLine($"\t\t\tIsReplacible = {block.IsReplacible.ToString().ToLower()};");
			builder.AppendLine($"\t\t\tIsFullBlock = {block.IsFullBlock.ToString().ToLower()};");
			builder.AppendLine($"\t\t\tIsFullCube = {block.IsFullCube.ToString().ToLower()};");

			if (block.LightOpacity != 0)
			{
				builder.AppendLine($"\t\t\tLightOpacity = {block.LightOpacity};");
			}

			if (block.Drag > 0)
			{
				builder.AppendLine($"\t\t\tDrag = {block.Drag:F}f;");
			}

			if (block.LightValue > 0)
			{
				builder.AppendLine($"\t\t\tLightValue = {block.LightValue};");
			}

			builder.AppendLine("\t\t}");
			builder.AppendLine("\t}");

			builder.AppendLine("}");

			if (hasMeta)
			{
				File.WriteAllBytes($"generated\\blocks\\withMeta\\{className}.cs", Encoding.UTF8.GetBytes(builder.ToString()));
			}
			else
			{
				File.WriteAllBytes($"generated\\blocks\\{className}.cs", Encoding.UTF8.GetBytes(builder.ToString()));
			}
		}

		public static string ToPascalCase(string original)
		{
			Regex invalidCharsRgx = new Regex("[^_a-zA-Z0-9]");
			Regex whiteSpace = new Regex(@"(?<=\s)");
			Regex startsWithLowerCaseChar = new Regex("^[a-z]");
			Regex firstCharFollowedByUpperCasesOnly = new Regex("(?<=[A-Z])[A-Z0-9]+$");
			Regex lowerCaseNextToNumber = new Regex("(?<=[0-9])[a-z]");
			Regex upperCaseInside = new Regex("(?<=[A-Z])[A-Z]+?((?=[A-Z][a-z])|(?=[0-9]))");

			// replace white spaces with undescore, then replace all invalid chars with empty string
			var pascalCase = invalidCharsRgx.Replace(whiteSpace.Replace(original, "_"), string.Empty)
				// split by underscores
				.Split(new char[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
				// set first letter to uppercase
				.Select(w => startsWithLowerCaseChar.Replace(w, m => m.Value.ToUpper()))
				// replace second and all following upper case letters to lower if there is no next lower (ABC -> Abc)
				.Select(w => firstCharFollowedByUpperCasesOnly.Replace(w, m => m.Value.ToLower()))
				// set upper case the first lower case following a number (Ab9cd -> Ab9Cd)
				.Select(w => lowerCaseNextToNumber.Replace(w, m => m.Value.ToUpper()))
				// lower second and next upper case letters except the last if it follows by any lower (ABcDEf -> AbcDef)
				.Select(w => upperCaseInside.Replace(w, m => m.Value.ToLower()));

			return string.Concat(pascalCase);
		}

		private static BlockModel ResolveModel(ResourceManager resources, McResourcePack resourcePack,
			IBlockState state)
		{
			string name = state.Name;

			if (string.IsNullOrWhiteSpace(name))
			{
				Log.Warn($"State name is null!");
				return null;
			}

			if (name.Contains("water"))
			{
				return StationairyWaterModel;
			}

			if (name.Contains("lava"))
			{
				return StationairyLavaModel;
			}

			BlockState blockState;

			if (resourcePack.BlockStates.TryGetValue(name, out blockState))
			{
				if (blockState != null && blockState.Parts != null && blockState.Parts.Length > 0)
				{
					return new MultiStateResourcePackModel(resources, blockState);
				}

				if (blockState.Variants == null ||
					blockState.Variants.Count == 0)
					return null;

				if (blockState.Variants.Count == 1)
				{
					var v = blockState.Variants.FirstOrDefault();
					return new CachedResourcePackModel(resources, new[] { v.Value.FirstOrDefault() });
				}

				BlockStateVariant blockStateVariant = null;

				var data = state.ToDictionary();
				int closestMatch = 0;
				KeyValuePair<string, BlockStateVariant> closest = default(KeyValuePair<string, BlockStateVariant>);
				foreach (var v in blockState.Variants)
				{
					int matches = 0;
					var variantBlockState = Blocks.State.BlockState.FromString(v.Key);
				
					foreach (var kv in data)
					{
						if (variantBlockState.TryGetValue(kv.Key.Name, out string vValue))
						{
							if (vValue.Equals(kv.Value, StringComparison.InvariantCultureIgnoreCase))
							{
								matches++;
							}
						}
					}

					if (matches > closestMatch)
					{
						closestMatch = matches;
						closest = v;
					}
				}

				blockStateVariant = closest.Value;

				if (blockStateVariant == null)
				{
					var a = blockState.Variants.FirstOrDefault();
					blockStateVariant = a.Value;
				}


				var subVariant = blockStateVariant.FirstOrDefault();
				return new CachedResourcePackModel(resources, new[] { subVariant });
			}

			return null;
		}

		private static readonly IBlockState AirState = new Blocks.State.BlockState();

		public static IBlockState GetBlockState(int blockId, byte meta)
		{
			return GetBlockState(GetBlockStateID(blockId, meta));
		}

		public static IBlockState GetBlockState(string palleteId)
		{
			if (BlockStateByName.TryGetValue(palleteId, out var result))
			{
				return result;
			}

			return AirState;
		}

		public static IBlockState GetBlockState(uint palleteId)
		{
			if (RegisteredBlockStates.TryGetValue(palleteId, out var result))
			{
				return result;
			}

			return AirState;
		}

		public static IBlockState GetBlockState(int palleteId)
		{
			if (RegisteredBlockStates.TryGetValue((uint)palleteId, out var result))
			{
				return result;
			}

			return AirState;
		}

		public static uint GetBlockStateId(IBlockState state)
		{
			var first = RegisteredBlockStates.FirstOrDefault(x => x.Value.Equals(state)).Key;

			return first;

		}

		//TODO: Categorize and implement
		private static Block GetBlockByName(string blockName)
		{
			blockName = blockName.ToLowerInvariant();
			if (string.IsNullOrWhiteSpace(blockName)) return null;
			else if (blockName == "minecraft:air" || blockName == "air") return new Air();
			else if (blockName == "minecraft:cave_air" || blockName == "caveair") return new Air();

			else if (blockName == "minecraft:stone" || blockName == "stone") return new Stone();
			else if (blockName == "minecraft:dirt" || blockName == "dirt") return new Dirt();
			else if (blockName == "minecraft:podzol" || blockName == "podzol") return new Podzol();
			else if (blockName == "minecraft:cobblestone" || blockName == "cobblestone") return new Cobblestone();
			else if (blockName == "minecraft:bedrock" || blockName == "bedrock") return new Bedrock();
			else if (blockName == "minecraft:sand" || blockName == "sand") return new Sand();
			else if (blockName == "minecraft:gravel" || blockName == "gravel") return new Gravel();
			else if (blockName == "minecraft:sponge" || blockName == "sponge") return new Sponge();
			else if (blockName == "minecraft:glass" || blockName == "glass") return new Glass();
			else if (blockName == "minecraft:dispenser" || blockName == "dispenser") return new Dispenser();
			else if (blockName == "minecraft:sandstone" || blockName == "sandstone") return new Sandstone();
			else if (blockName == "minecraft:note_block" || blockName == "noteblock") return new NoteBlock();
			else if (blockName == "minecraft:detector_rail" || blockName == "detectorrail") return new DetectorRail();
			else if (blockName == "minecraft:grass" || blockName == "grass") return new Grass();
			else if (blockName == "minecraft:fern" || blockName == "fern") return new Fern();
			else if (blockName == "minecraft:large_fern" || blockName == "largefern") return new Fern(); //TODO: Create large fern class
			else if (blockName == "minecraft:brown_mushroom" || blockName == "brownmushroom") return new BrownMushroom();
			else if (blockName == "minecraft:red_mushroom" || blockName == "redmushroom") return new RedMushroom();
			else if (blockName == "minecraft:dead_bush" || blockName == "deadbush") return new DeadBush();
			else if (blockName == "minecraft:piston" || blockName == "piston") return new Piston();
			else if (blockName == "minecraft:piston_head" || blockName == "pistonhead") return new PistonHead();
			else if (blockName == "minecraft:sticky_piston" || blockName == "stickypiston") return new StickyPiston();
			else if (blockName == "minecraft:tnt" || blockName == "tnt") return new Tnt();
			else if (blockName == "minecraft:bookshelf" || blockName == "bookshelf") return new Bookshelf();
			else if (blockName == "minecraft:mossy_cobblestone" || blockName == "mossycobblestone") return new MossyCobblestone();
			else if (blockName == "minecraft:obsidian" || blockName == "obsidian") return new Obsidian();
			else if (blockName == "minecraft:fire" || blockName == "fire") return new Fire();
			else if (blockName == "minecraft:mob_spawner" || blockName == "mobspawner") return new MobSpawner();
			else if (blockName == "minecraft:chest" || blockName == "chest") return new Chest();
			else if (blockName == "minecraft:redstone_wire" || blockName == "redstonewire") return new RedstoneWire();
			else if (blockName == "minecraft:crafting_table" || blockName == "craftingtable") return new CraftingTable();
			else if (blockName == "minecraft:wheat" || blockName == "wheat") return new Wheat();
			else if (blockName == "minecraft:farmland" || blockName == "farmland") return new Farmland();
			else if (blockName == "minecraft:furnace" || blockName == "furnace") return new Furnace();
			else if (blockName == "minecraft:ladder" || blockName == "ladder") return new Ladder();
			else if (blockName == "minecraft:rail" || blockName == "rail") return new Rail();
			else if (blockName == "minecraft:wall_sign" || blockName == "wallsign") return new WallSign();
			else if (blockName == "minecraft:lever" || blockName == "lever") return new Lever();
			else if (blockName == "minecraft:stone_pressure_plate" || blockName == "stonepressureplate") return new StonePressurePlate();
			else if (blockName == "minecraft:torch" || blockName == "torch") return new Torch();
			else if (blockName == "minecraft:wall_torch" || blockName == "walltorch") return new Torch(true); 
			else if (blockName == "minecraft:redstone_torch" || blockName == "redstonetorch") return new RedstoneTorch();
			else if (blockName == "minecraft:snow" || blockName == "snow") return new Snow();
			else if (blockName == "minecraft:ice" || blockName == "ice") return new Ice();
			else if (blockName == "minecraft:cactus" || blockName == "cactus") return new Cactus();
			else if (blockName == "minecraft:clay" || blockName == "clay") return new Clay();
			else if (blockName == "minecraft:pumpkin" || blockName == "pumpkin") return new Pumpkin();
			else if (blockName == "minecraft:netherrack" || blockName == "netherrack") return new Netherrack();
			else if (blockName == "minecraft:soul_sand" || blockName == "soulsand") return new SoulSand();
			else if (blockName == "minecraft:glowstone" || blockName == "glowstone") return new Glowstone();
			else if (blockName == "minecraft:portal" || blockName == "portal") return new Portal();
			else if (blockName == "minecraft:cake" || blockName == "cake") return new Cake();
			else if (blockName == "minecraft:brown_mushroom_block" || blockName == "brownmushroomblock") return new BrownMushroomBlock();
			else if (blockName == "minecraft:red_mushroom_block" || blockName == "redmushroomblock") return new RedMushroomBlock();
			else if (blockName == "minecraft:iron_bars" || blockName == "ironbars") return new IronBars();
			else if (blockName == "minecraft:glass_pane" || blockName == "glasspane") return new GlassPane();
			else if (blockName == "minecraft:melon_block" || blockName == "melonblock") return new MelonBlock();
			else if (blockName == "minecraft:pumpkin_stem" || blockName == "pumpkinstem") return new PumpkinStem();
			else if (blockName == "minecraft:melon_stem" || blockName == "melonstem") return new MelonStem();
			else if (blockName == "minecraft:vine" || blockName == "vine") return new Vine();
			else if (blockName == "minecraft:mycelium" || blockName == "mycelium") return new Mycelium();
			else if (blockName == "minecraft:nether_wart" || blockName == "netherwart") return new NetherWart();
			else if (blockName == "minecraft:enchanting_table" || blockName == "enchantingtable") return new EnchantingTable();
			else if (blockName == "minecraft:brewing_stand" || blockName == "brewingstand") return new BrewingStand();
			else if (blockName == "minecraft:cauldron" || blockName == "cauldron") return new Cauldron();
			else if (blockName == "minecraft:end_portal" || blockName == "endportal") return new EndPortal();
			else if (blockName == "minecraft:end_portal_frame" || blockName == "endportalframe") return new EndPortalFrame();
			else if (blockName == "minecraft:end_stone" || blockName == "endstone") return new EndStone();
			else if (blockName == "minecraft:dragon_egg" || blockName == "dragonegg") return new DragonEgg();
			else if (blockName == "minecraft:redstone_lamp" || blockName == "redstonelamp") return new RedstoneLamp();
			else if (blockName == "minecraft:cocoa" || blockName == "cocoa") return new Cocoa();
			else if (blockName == "minecraft:ender_chest" || blockName == "enderchest") return new EnderChest();
			else if (blockName == "minecraft:tripwire_hook" || blockName == "tripwirehook") return new TripwireHook();
			else if (blockName == "minecraft:tripwire" || blockName == "tripwire") return new Tripwire();
			else if (blockName == "minecraft:beacon" || blockName == "beacon") return new Beacon();
			else if (blockName == "minecraft:cobblestone_wall" || blockName == "cobblestonewall") return new CobblestoneWall();
			else if (blockName == "minecraft:flower_pot" || blockName == "flowerpot") return new FlowerPot();
			else if (blockName == "minecraft:carrots" || blockName == "carrots") return new Carrots();
			else if (blockName == "minecraft:potatoes" || blockName == "potatoes") return new Potatoes();
			else if (blockName == "minecraft:anvil" || blockName == "anvil") return new Anvil();
			else if (blockName == "minecraft:trapped_chest" || blockName == "trappedchest") return new TrappedChest();
			else if (blockName == "minecraft:light_weighted_pressure_plate" || blockName == "lightweightedpressureplate") return new LightWeightedPressurePlate();
			else if (blockName == "minecraft:heavy_weighted_pressure_plate" || blockName == "heavyweightedpressureplate") return new HeavyWeightedPressurePlate();
			else if (blockName == "minecraft:daylight_detector" || blockName == "daylightdetector") return new DaylightDetector();
			else if (blockName == "minecraft:redstone_block" || blockName == "redstoneblock") return new RedstoneBlock();
			else if (blockName == "minecraft:hopper" || blockName == "hopper") return new Hopper();
			else if (blockName == "minecraft:quartz_block" || blockName == "quartzblock") return new QuartzBlock();
			else if (blockName == "minecraft:activator_rail" || blockName == "activatorrail") return new ActivatorRail();
			else if (blockName == "minecraft:dropper" || blockName == "dropper") return new Dropper();
			else if (blockName == "minecraft:iron_trapdoor" || blockName == "irontrapdoor") return new IronTrapdoor();
			else if (blockName == "minecraft:prismarine" || blockName == "prismarine") return new Prismarine();
			else if (blockName == "minecraft:sea_lantern" || blockName == "sealantern") return new SeaLantern();
			else if (blockName == "minecraft:hay_block" || blockName == "hayblock") return new HayBlock();
			else if (blockName == "minecraft:coal_block" || blockName == "coalblock") return new CoalBlock();
			else if (blockName == "minecraft:packed_ice" || blockName == "packedice") return new PackedIce();
			else if (blockName == "minecraft:tall_grass" || blockName == "tallgrass") return new TallGrass();
			else if (blockName == "minecraft:red_sandstone" || blockName == "redsandstone") return new RedSandstone();
			else if (blockName == "minecraft:end_rod" || blockName == "endrod") return new EndRod();
			else if (blockName == "minecraft:chorus_plant" || blockName == "chorusplant") return new ChorusPlant();
			else if (blockName == "minecraft:chorus_flower" || blockName == "chorusflower") return new ChorusFlower();
			else if (blockName == "minecraft:purpur_block" || blockName == "purpurblock") return new PurpurBlock();
			else if (blockName == "minecraft:grass_path" || blockName == "grasspath") return new GrassPath();
			else if (blockName == "minecraft:end_gateway" || blockName == "endgateway") return new EndGateway();
			else if (blockName == "minecraft:frosted_ice" || blockName == "frostedice") return new FrostedIce();
			else if (blockName == "minecraft:observer" || blockName == "observer") return new Observer();
			else if (blockName == "minecraft:grass_block" || blockName == "grassblock") return new GrassBlock();
			else if (blockName == "minecraft:powered_rail" || blockName == "poweredrail") return new PoweredRail();
			else if (blockName == "minecraft:bricks" || blockName == "bricks") return new Bricks();
			else if (blockName == "minecraft:cobweb" || blockName == "cobweb") return new Cobweb();
			else if (blockName == "minecraft:dandelion" || blockName == "dandelion") return new Dandelion();
			else if (blockName == "minecraft:poppy" || blockName == "poppy") return new Poppy();
			else if (blockName == "minecraft:sugar_cane" || blockName == "sugarcane") return new SugarCane();
			else if (blockName == "minecraft:beetroots" || blockName == "beetroots") return new Beetroots();
			else if (blockName == "minecraft:nether_wart_block" || blockName == "netherwartblock") return new NetherWartBlock();
			else if (blockName == "minecraft:jukebox" || blockName == "jukebox") return new Jukebox();
			else if (blockName == "minecraft:stone_bricks" || blockName == "stonebricks") return new StoneBricks();
			else if (blockName == "minecraft:lily_pad" || blockName == "lilypad") return new LilyPad();
			else if (blockName == "minecraft:command_block" || blockName == "commandblock") return new CommandBlock();
			else if (blockName == "minecraft:nether_quartz_ore" || blockName == "netherquartzore") return new NetherQuartzOre();
			else if (blockName == "minecraft:slime_block" || blockName == "slimeblock") return new SlimeBlock();
			else if (blockName == "minecraft:purpur_pillar" || blockName == "purpurpillar") return new PurpurPillar();
			else if (blockName == "minecraft:end_stone_bricks" || blockName == "endstonebricks") return new EndStoneBricks();
			else if (blockName == "minecraft:repeating_command_block" || blockName == "repeatingcommandblock") return new RepeatingCommandBlock();
			else if (blockName == "minecraft:chain_command_block" || blockName == "chaincommandblock") return new ChainCommandBlock();
			else if (blockName == "minecraft:magma_block" || blockName == "magmablock") return new MagmaBlock();
			else if (blockName == "minecraft:bone_block" || blockName == "boneblock") return new BoneBlock();
			else if (blockName == "minecraft:structure_block" || blockName == "structureblock") return new StructureBlock();

			//Buttons
			else if (blockName == "minecraft:stone_button" || blockName == "stonebutton") return new StoneButton();
			else if (blockName == "minecraft:oak_button" || blockName == "oakbutton") return new OakButton();
			else if (blockName == "minecraft:spruce_button" || blockName == "sprucebutton") return new SpruceButton();
			else if (blockName == "minecraft:birch_button" || blockName == "birchbutton") return new BirchButton();
			else if (blockName == "minecraft:jungle_button" || blockName == "junglebutton") return new JungleButton();
			else if (blockName == "minecraft:acacia_button" || blockName == "acaciabutton") return new AcaciaButton();
			else if (blockName == "minecraft:dark_oak_button" || blockName == "darkoakbutton") return new DarkOakButton();

			//Terracotta
			else if (blockName == "minecraft:white_glazed_terracotta" || blockName == "whiteglazedterracotta") return new WhiteGlazedTerracotta();
			else if (blockName == "minecraft:orange_glazed_terracotta" || blockName == "orangeglazedterracotta") return new OrangeGlazedTerracotta();
			else if (blockName == "minecraft:magenta_glazed_terracotta" || blockName == "magentaglazedterracotta") return new MagentaGlazedTerracotta();
			else if (blockName == "minecraft:light_blue_glazed_terracotta" || blockName == "lightblueglazedterracotta") return new LightBlueGlazedTerracotta();
			else if (blockName == "minecraft:yellow_glazed_terracotta" || blockName == "yellowglazedterracotta") return new YellowGlazedTerracotta();
			else if (blockName == "minecraft:lime_glazed_terracotta" || blockName == "limeglazedterracotta") return new LimeGlazedTerracotta();
			else if (blockName == "minecraft:pink_glazed_terracotta" || blockName == "pinkglazedterracotta") return new PinkGlazedTerracotta();
			else if (blockName == "minecraft:gray_glazed_terracotta" || blockName == "grayglazedterracotta") return new GrayGlazedTerracotta();
			else if (blockName == "minecraft:cyan_glazed_terracotta" || blockName == "cyanglazedterracotta") return new CyanGlazedTerracotta();
			else if (blockName == "minecraft:purple_glazed_terracotta" || blockName == "purpleglazedterracotta") return new PurpleGlazedTerracotta();
			else if (blockName == "minecraft:blue_glazed_terracotta" || blockName == "blueglazedterracotta") return new BlueGlazedTerracotta();
			else if (blockName == "minecraft:brown_glazed_terracotta" || blockName == "brownglazedterracotta") return new BrownGlazedTerracotta();
			else if (blockName == "minecraft:green_glazed_terracotta" || blockName == "greenglazedterracotta") return new GreenGlazedTerracotta();
			else if (blockName == "minecraft:red_glazed_terracotta" || blockName == "redglazedterracotta") return new RedGlazedTerracotta();
			else if (blockName == "minecraft:black_glazed_terracotta" || blockName == "blackglazedterracotta") return new BlackGlazedTerracotta();
			else if (blockName == "minecraft:light_gray_glazed_terracotta" || blockName == "lightgrayglazedterracotta") return new LightGrayGlazedTerracotta();

			//Doors
			else if (blockName == "minecraft:oak_door" || blockName == "oakdoor") return new OakDoor();
			else if (blockName == "minecraft:spruce_door" || blockName == "sprucedoor") return new SpruceDoor();
			else if (blockName == "minecraft:birch_door" || blockName == "birchdoor") return new BirchDoor();
			else if (blockName == "minecraft:jungle_door" || blockName == "jungledoor") return new JungleDoor();
			else if (blockName == "minecraft:acacia_door" || blockName == "acaciadoor") return new AcaciaDoor();
			else if (blockName == "minecraft:dark_oak_door" || blockName == "darkoakdoor") return new DarkOakDoor();
			else if (blockName == "minecraft:iron_door" || blockName == "irondoor") return new IronDoor();

			//Slabs
			else if (blockName == "minecraft:stone_slab" || blockName == "stoneslab") return new StoneSlab();
			else if (blockName == "minecraft:red_sandstone_slab" || blockName == "redsandstoneslab") return new RedSandstoneSlab();
			else if (blockName == "minecraft:purpur_slab" || blockName == "purpurslab") return new PurpurSlab();
			else if (blockName == "minecraft:prismarine_slab" || blockName == "prismarineslab") return new PrismarineSlab();
			else if (blockName == "minecraft:prismarine_bricks_slab" || blockName == "prismarinebricksslab") return new PrismarineBricksSlab();
			else if (blockName == "minecraft:dark_prismarine_slab" || blockName == "darkprismarineslab") return new DarkPrismarineSlab();
			else if (blockName == "minecraft:oak_slab" || blockName == "oakslab") return new OakSlab();
			else if (blockName == "minecraft:spruce_slab" || blockName == "spruceslab") return new SpruceSlab();
			else if (blockName == "minecraft:birch_slab" || blockName == "birchslab") return new BirchSlab();
			else if (blockName == "minecraft:jungle_slab" || blockName == "jungleslab") return new JungleSlab();
			else if (blockName == "minecraft:acacia_slab" || blockName == "acaciaslab") return new AcaciaSlab();
			else if (blockName == "minecraft:dark_oak_slab" || blockName == "darkoakslab") return new DarkOakSlab();
			else if (blockName == "minecraft:sandstone_slab" || blockName == "sandstoneslab") return new SandstoneSlab();
			else if (blockName == "minecraft:petrified_oak_slab" || blockName == "petrifiedoakslab") return new PetrifiedOakSlab();
			else if (blockName == "minecraft:cobblestone_slab" || blockName == "cobblestoneslab") return new CobblestoneSlab();
			else if (blockName == "minecraft:brick_slab" || blockName == "brickslab") return new BrickSlab();
			else if (blockName == "minecraft:stone_brick_slab" || blockName == "stonebrickslab") return new StoneBrickSlab();
			else if (blockName == "minecraft:nether_brick_slab" || blockName == "netherbrickslab") return new NetherBrickSlab();
			else if (blockName == "minecraft:quartz_slab" || blockName == "quartzslab") return new QuartzSlab();
			else if (blockName == "minecraft:red_sandstone_slab" || blockName == "redsandstoneslab") return new RedSandstoneSlab();
			else if (blockName == "minecraft:purpur_slab" || blockName == "purpurslab") return new PurpurSlab();

			//Leaves
			else if (blockName == "minecraft:oak_leaves" || blockName == "oakleaves") return new OakLeaves();
			else if (blockName == "minecraft:spruce_leaves" || blockName == "spruceleaves") return new SpruceLeaves();
			else if (blockName == "minecraft:birch_leaves" || blockName == "birchleaves") return new BirchLeaves();
			else if (blockName == "minecraft:jungle_leaves" || blockName == "jungleleaves") return new JungleLeaves();
			else if (blockName == "minecraft:acacia_leaves" || blockName == "acacialeaves") return new AcaciaLeaves();
			else if (blockName == "minecraft:dark_oak_leaves" || blockName == "darkoakleaves") return new DarkOakLeaves();

			//Fencing
			else if (blockName == "minecraft:nether_brick_fence" || blockName == "netherbrickfence") return new NetherBrickFence();
			else if (blockName == "minecraft:oak_fence" || blockName == "oakfence") return new OakFence();
			else if (blockName == "minecraft:oak_fence_gate" || blockName == "oakfencegate") return new FenceGate();
			else if (blockName == "minecraft:dark_oak_fence_gate" || blockName == "darkoakfencegate") return new DarkOakFenceGate();
			else if (blockName == "minecraft:spruce_fence_gate" || blockName == "sprucefencegate") return new SpruceFenceGate();
			else if (blockName == "minecraft:birch_fence_gate" || blockName == "birchfencegate") return new BirchFenceGate();
			else if (blockName == "minecraft:birch_fence" || blockName == "birchfence") return new BirchFence();
			else if (blockName == "minecraft:jungle_fence_gate" || blockName == "junglefencegate") return new JungleFenceGate();
			else if (blockName == "minecraft:acacia_fence_gate" || blockName == "acaciafencegate") return new AcaciaFenceGate();

			//Stairs
			else if (blockName == "minecraft:purpur_stairs" || blockName == "purpurstairs") return new PurpurStairs();
			else if (blockName == "minecraft:cobblestone_stairs" || blockName == "cobblestonestairs") return new CobblestoneStairs();
			else if (blockName == "minecraft:acacia_stairs" || blockName == "acaciastairs") return new AcaciaStairs();
			else if (blockName == "minecraft:dark_oak_stairs" || blockName == "darkoakstairs") return new DarkOakStairs();
			else if (blockName == "minecraft:quartz_stairs" || blockName == "quartzstairs") return new QuartzStairs();
			else if (blockName == "minecraft:red_sandstone_stairs" || blockName == "redsandstonestairs") return new RedSandstoneStairs();
			else if (blockName == "minecraft:spruce_stairs" || blockName == "sprucestairs") return new SpruceStairs();
			else if (blockName == "minecraft:birch_stairs" || blockName == "birchstairs") return new BirchStairs();
			else if (blockName == "minecraft:jungle_stairs" || blockName == "junglestairs") return new JungleStairs();
			else if (blockName == "minecraft:sandstone_stairs" || blockName == "sandstonestairs") return new SandstoneStairs();
			else if (blockName == "minecraft:brick_stairs" || blockName == "brickstairs") return new BrickStairs();
			else if (blockName == "minecraft:stone_brick_stairs" || blockName == "stonebrickstairs") return new StoneBrickStairs();
			else if (blockName == "minecraft:nether_brick_stairs" || blockName == "netherbrickstairs") return new NetherBrickStairs();
			else if (blockName == "minecraft:oak_stairs" || blockName == "oakstairs") return new OakStairs();

			//Liquid
			else if (blockName == "minecraft:water" || blockName == "water") return new Water();
			else if (blockName == "minecraft:lava" || blockName == "lava") return new Lava();
			else if (blockName == "minecraft:kelp" || blockName == "kelp") return new Kelp();

			//Ores
			else if (blockName == "minecraft:redstone_ore" || blockName == "redstoneore") return new RedstoneOre();
			else if (blockName == "minecraft:gold_ore" || blockName == "goldore") return new GoldOre();
			else if (blockName == "minecraft:iron_ore" || blockName == "ironore") return new IronOre();
			else if (blockName == "minecraft:coal_ore" || blockName == "coalore") return new CoalOre();
			else if (blockName == "minecraft:diamond_ore" || blockName == "diamondore") return new DiamondOre();
			else if (blockName == "minecraft:emerald_ore" || blockName == "emeraldore") return new EmeraldOre();
			else if (blockName == "minecraft:lapis_ore" || blockName == "lapisore") return new LapisOre();

			else if (blockName == "minecraft:gold_block" || blockName == "goldblock") return new GoldBlock();
			else if (blockName == "minecraft:iron_block" || blockName == "ironblock") return new IronBlock();
			else if (blockName == "minecraft:diamond_block" || blockName == "diamondblock") return new DiamondBlock();
			else if (blockName == "minecraft:emerald_block" || blockName == "emeraldblock") return new EmeraldBlock();
			else if (blockName == "minecraft:lapis_block" || blockName == "lapisblock") return new LapisBlock();

			else return null;
		}

		private static Block Air { get; } = new Air();
		public static Block GetBlock(uint palleteId)
		{
			if (palleteId == 0) return Air;
			if (RegisteredBlockStates.TryGetValue(palleteId, out IBlockState b))
			{
				return (Block) b.GetBlock();
			}

			return new Block(palleteId)
			{
				BlockModel = new CachedResourcePackModel(null, new[] { new BlockStateModel
				{
					Model = CubeModel,
					ModelName = CubeModel.Name,
					Y = 0,
					X = 0,
					Uvlock = false,
					Weight = 0
				}}),
				Transparent = false,
				DisplayName = "Unknown"
			};
		}

		public static Block GetBlock(int id, byte metadata)
		{
			if (id == 0) return Air;
			return GetBlock(GetBlockStateID(id, metadata));
		}

		public static uint GetBlockStateID(int id, byte meta)
		{
			if (id < 0) throw new ArgumentOutOfRangeException();

			return (uint)(id << 4 | meta);
		}

		public static void StateIDToRaw(uint stateId, out int id, out byte meta)
		{
			id = (int)(stateId >> 4);
			meta = (byte)(stateId & 0x0F);
		}

		private class BlockMeta
		{
			public int ID = -1;
			public string Name;
			public string DisplayName;
			public bool Transparent;
			public bool IsFullBlock;
			public double AmbientOcclusionLightValue = 1.0;
			public int LightValue;
			public int LightOpacity;
			public bool IsBlockNormalCube;
			public bool IsFullCube;
			public bool Solid;
			public float FrictionFactor;

			public Dictionary<string, bool> IsSideSolid;
			public bool Replacible;
		}
	}
}
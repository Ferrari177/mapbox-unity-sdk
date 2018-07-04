namespace Mapbox.Unity.MeshGeneration.Factories
{
	using Mapbox.Platform;
	using Mapbox.Unity.MeshGeneration.Data;
	using System;
	using UnityEngine;
	using Mapbox.Unity.Map;
	using Mapbox.Map;
	using System.Collections.Generic;

	/// <summary>
	/// Factories
	/// Factories corresponds to Mapbox Api endpoints for the most part.Terrain factory uses terrain rgb api, 
	/// Image factory uses raster image api and vector tile factory uses vector data api.
	/// Only exception to this at the moment is Directions factory, which is a monobehaviour in Drive demo scene 
	/// but we’ll probably rework that in the future as well.
	/// Factories do the api calls.They are responsible for reporting progress and logging/reporting any network issue. 
	/// They can also keep track of tiles if necessary (i.e.update/refresh data after initial creation)
	/// Factories processes the received data in different ways.
	/// Terrain factories creates the mesh from the received data (or a flat terrain depending the settings)
	/// MapImage factory applies received image to tile game object.
	/// Vector Tile Factory deals with a much more complex and detailed data compared to other two so it doesn’t do the 
	/// whole processing itself and uses some subclasses (LayerVisualizers) to do it.
	/// Creating a custom factory is a good idea if you want to fetch raw data and process is in a totally different 
	/// custom way, like creating terrain and then cut off water areas from terrain mesh.
	/// Vector Tile factory, for example, is built to be flexible and work with multiple vector layers. But if you 
	/// don’t need that all, you can create a custom version of it and process what you need in a much more concrete 
	/// and performant way.
	/// Another example here would be custom terrain mesh. Current terrain factories work with a custom sized grid 
	/// and apply height data on that. By creating a custom terrain factory, you can have a custom mesh instead of a grid, 
	/// optimize and minimize vertex count etc.
	/// </summary>
	public abstract class AbstractTileFactory : ScriptableObject
	{
		protected IFileSource _fileSource;

		public ModuleState State { get; private set; }

		protected LayerProperties _options;
		public LayerProperties Options
		{
			get
			{
				return _options;
			}
		}
		
		protected Queue<UnityTile> _tilesToFetch;
		protected HashSet<UnityTile> _tilesWaitingResponse;
		protected HashSet<UnityTile> _tilesWaitingProcessing;

		public event Action<AbstractTileFactory> OnFactoryStateChanged = delegate { };
		/// <summary>
		/// The  <c>OnTileError</c> event triggers when there's <c>Tile</c> error.
		/// Returns a <see cref="T:Mapbox.Map.TileErrorEventArgs"/> instance as a parameter, for the tile on which error occurred.
		/// </summary>
		public event EventHandler<TileErrorEventArgs> OnTileError;

		public virtual void SetOptions(LayerProperties options)
		{
			_options = options;
		}

		protected virtual void OnErrorOccurred(TileErrorEventArgs e)
		{
			EventHandler<TileErrorEventArgs> handler = OnTileError;
			if (handler != null)
			{
				handler(this, e);
			}
		}

		public virtual void Initialize(IFileSource fileSource)
		{
			_fileSource = fileSource;
			State = ModuleState.Initialized;
			_tilesToFetch = new Queue<UnityTile>();
			_tilesWaitingResponse = new HashSet<UnityTile>();
			_tilesWaitingProcessing = new HashSet<UnityTile>();
			OnInitialized();
		}

		public virtual void MapUpdate()
		{
			if (State == ModuleState.Initialized || State == ModuleState.Working)
			{
				if(_tilesToFetch.Count == 0 && _tilesWaitingResponse.Count == 0 && _tilesWaitingProcessing.Count == 0)
				{
					State = ModuleState.Finished;
					OnFactoryStateChanged(this);
				}
			}
			else if (State == ModuleState.Finished)
			{
				if (_tilesToFetch.Count > 0 || _tilesWaitingResponse.Count > 0 || _tilesWaitingProcessing.Count > 0)
				{
					State = ModuleState.Working;
					OnFactoryStateChanged(this);
				}
			}


			OnMapUpdate();
		}

		public virtual void Register(UnityTile tile)
		{
			OnRegistered(tile);
		}

		public virtual void Unregister(UnityTile tile)
		{
			OnUnregistered(tile);
		}

		protected abstract void OnMapUpdate();

		protected abstract void OnInitialized();

		protected abstract void OnRegistered(UnityTile tile);

		protected abstract void OnUnregistered(UnityTile tile);
	}
}

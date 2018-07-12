namespace Mapbox.Unity.MeshGeneration.Data
{
	using UnityEngine;
	using Mapbox.Unity.MeshGeneration.Enums;
	using Mapbox.Unity.Utilities;
	using Utils;
	using Mapbox.Map;
	using System;
	using Mapbox.Unity.Map;
	using System.Collections.Generic;
	using Mapbox.Unity.MeshGeneration.Factories;

	public class UnityTile : MonoBehaviour
	{
		[SerializeField]
		public Texture2D RasterData;
		public VectorTile VectorData;
		private Texture2D _heightTexture;
		private float[] _heightData;

		private Texture2D _loadingTexture;
		//keeping track of tile objects to be able to cancel them safely if tile is destroyed before data fetching finishes
		private List<Tile> _tiles = new List<Tile>();
		//keeping track of factories to know when tile is finished or ready for meshgen
		private HashSet<AbstractTileFactory> _workingFactories = new HashSet<AbstractTileFactory>();

		#region CachedUnityComponents
		MeshRenderer _meshRenderer;
		public MeshRenderer MeshRenderer
		{
			get
			{
				if (_meshRenderer == null)
				{
					_meshRenderer = gameObject.AddComponent<MeshRenderer>();
				}
				return _meshRenderer;
			}
		}

		private MeshFilter _meshFilter;
		public MeshFilter MeshFilter
		{
			get
			{
				if (_meshFilter == null)
				{
					_meshFilter = gameObject.AddComponent<MeshFilter>();
				}
				return _meshFilter;
			}
		}

		private Collider _collider;
		public Collider Collider
		{
			get
			{
				if (_collider == null)
				{
					_collider = GetComponent<Collider>();
				}
				return _collider;
			}
		}
		#endregion

		#region Tile Positon/Scale Properties
		public RectD Rect { get; private set; }
		public int InitialZoom { get; private set; }
		public float TileScale { get; private set; }
		public UnwrappedTileId UnwrappedTileId { get; private set; }
		public CanonicalTileId CanonicalTileId { get; private set; }

		private float _relativeScale;
		#endregion

		[SerializeField]
		private TilePropertyState _rasterDataState;
		public TilePropertyState RasterDataState { get { return _rasterDataState; } }
		[SerializeField]
		private TilePropertyState _heightDataState;
		public TilePropertyState HeightDataState { get { return _heightDataState; } }
		[SerializeField]
		private TilePropertyState _vectorDataState;
		public TilePropertyState VectorDataState { get { return _vectorDataState; } }
		public bool IsReadyForVectorMeshGeneration = true;

		internal void Initialize(IMapReadable map, UnwrappedTileId tileId, float scale, int zoom, Texture2D loadingTexture = null)
		{
			TileScale = scale;
			_relativeScale = 1 / Mathf.Cos(Mathf.Deg2Rad * (float)map.CenterLatitudeLongitude.x);
			Rect = Conversions.TileBounds(tileId);
			UnwrappedTileId = tileId;
			CanonicalTileId = tileId.Canonical;
			_loadingTexture = loadingTexture;

			float scaleFactor = 1.0f;
			if (InitialZoom == 0)
			{
				InitialZoom = zoom;
			}

			scaleFactor = Mathf.Pow(2, (map.InitialZoom - zoom));
			gameObject.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
			gameObject.SetActive(true);
		}

		#region SetTileData
		public void SetHeightData(byte[] data, float heightMultiplier = 1f, bool useRelative = false, bool addCollider = false)
		{
			// HACK: compute height values for terrain. We could probably do this without a texture2d.
			if (_heightTexture == null)
			{
				_heightTexture = new Texture2D(0, 0);
			}

			_heightTexture.LoadImage(data);
			byte[] rgbData = _heightTexture.GetRawTextureData();

			// Get rid of this temporary texture. We don't need to bloat memory.
			_heightTexture.LoadImage(null);

			if (_heightData == null)
			{
				_heightData = new float[256 * 256];
			}

			var relativeScale = useRelative ? _relativeScale : 1f;
			for (int xx = 0; xx < 256; ++xx)
			{
				for (int yy = 0; yy < 256; ++yy)
				{
					float r = rgbData[(xx * 256 + yy) * 4 + 1];
					float g = rgbData[(xx * 256 + yy) * 4 + 2];
					float b = rgbData[(xx * 256 + yy) * 4 + 3];
					_heightData[xx * 256 + yy] = relativeScale * heightMultiplier * Conversions.GetAbsoluteHeightFromColor(r, g, b);
				}
			}

			if (addCollider && Collider == null)
			{
				gameObject.AddComponent<MeshCollider>();
			}

			_heightDataState = TilePropertyState.Finished;
			OnHeightDataChanged(this);

			if (RasterData != null)
			{
				MeshRenderer.material.mainTexture = RasterData;
			}
		}

		public void SetRasterData(byte[] data, bool useMipMap, bool useCompression)
		{
			// Don't leak the texture, just reuse it.
			if (RasterData == null)
			{
				RasterData = new Texture2D(0, 0, TextureFormat.RGB24, useMipMap);
				RasterData.wrapMode = TextureWrapMode.Clamp;
			}

			RasterData.LoadImage(data);
			if (useCompression)
			{
				// High quality = true seems to decrease image quality?
				RasterData.Compress(false);
			}

			if(MeshRenderer != null)
				MeshRenderer.material.mainTexture = RasterData;
			_rasterDataState = TilePropertyState.Finished;
			OnRasterDataChanged(this);
		}

		public void SetVectorData(VectorTile vectorTile)
		{
			VectorData = vectorTile;
		}
		#endregion

		#region GetTileData
		//keeping image and vector data is just regular public fields, not to add unnecessary method call overhead
		//shouldn't be set from outside though. might change to private set props just to prevent that

		//getting height data by single point queries. do we need anything different?
		public float QueryHeightData(float x, float y)
		{
			if (_heightData != null)
			{
				var intX = (int)Mathf.Clamp(x * 256, 0, 255);
				var intY = (int)Mathf.Clamp(y * 256, 0, 255);
				return _heightData[intY * 256 + intX] * TileScale;
			}

			return 0;
		}
		#endregion
		
		public void SetLoadingTexture(Texture2D texture)
		{
			MeshRenderer.material.mainTexture = texture;
		}

		public void AttachTile(Tile tile)
		{
			_tiles.Add(tile);
		}

		public void AddFactory(AbstractTileFactory factory)
		{
			_workingFactories.Add(factory);
			if (factory is MapImageFactory)
			{
				IsReadyForVectorMeshGeneration = false;
				if (factory is TerrainFactoryBase)
				{
					_heightDataState = TilePropertyState.Working;
				}
				else
				{
					_rasterDataState = TilePropertyState.Working;
				}
			}
			else if (factory is VectorTileFactory)
			{
				_vectorDataState = TilePropertyState.Working;
			}
		}

		public void RemoveFactory(AbstractTileFactory factory)
		{
			_workingFactories.Remove(factory);

			var readyForVector = true;
			foreach (var item in _workingFactories)
			{
				if (!(item is VectorTileFactory))
				{
					readyForVector = false;
					break;
				}
			}
			if (readyForVector)
			{
				if(!IsReadyForVectorMeshGeneration)
				{
					IsReadyForVectorMeshGeneration = readyForVector;
					OnReadyForMeshGeneration(this);
				}
			}
			IsReadyForVectorMeshGeneration = readyForVector;

			if (_workingFactories.Count == 0)
			{
				_vectorDataState = TilePropertyState.Finished;
				OnTileFinished(this);
			}
		}
		
		#region Cancel/Recycle/Destroy
		public void Cancel()
		{
			for (int i = 0, _tilesCount = _tiles.Count; i < _tilesCount; i++)
			{
				_tiles[i].Cancel();
			}
		}

		public void Recycle()
		{
			if (_loadingTexture && MeshRenderer != null)
			{
				MeshRenderer.material.mainTexture = _loadingTexture;
			}

			gameObject.SetActive(false);

			// Reset internal state.
			_rasterDataState = TilePropertyState.None;
			_heightDataState = TilePropertyState.None;
			_vectorDataState = TilePropertyState.None;

			OnHeightDataChanged = delegate { };
			OnRasterDataChanged = delegate { };
			OnVectorDataChanged = delegate { };

			Cancel();
			_tiles.Clear();
		}

		protected virtual void OnDestroy()
		{
			Cancel();
			if (_heightTexture != null)
			{
				Destroy(_heightTexture);
			}
			if (RasterData != null)
			{
				Destroy(RasterData);
			}
		}
		#endregion

		#region Events
		public event Action<UnityTile> OnHeightDataChanged = delegate { };
		public event Action<UnityTile> OnRasterDataChanged = delegate { };
		public event Action<UnityTile> OnVectorDataChanged = delegate { };
		public event Action<UnityTile> OnReadyForMeshGeneration = delegate { };
		public event Action<UnityTile> OnTileFinished = delegate { };
		#endregion
	}
}

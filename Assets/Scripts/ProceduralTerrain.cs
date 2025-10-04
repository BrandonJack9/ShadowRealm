using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Terrain))]
public class ProceduralTerrain : MonoBehaviour
{
    [Header("General Settings")]
    public float terrainHeight = 100f;
    public int seed = 12345; // same seed = consistent world
    public Transform waterObject;
    public float waterOffset = 1f;


    [Header("Global Offset")]
    public float globalOffset = -.1f;

    [Header("Macro Noise")]
    public float macroScale = 300f;
    public float macroAmplitude = 0.47f;

    [Header("Mid Noise")]
    public float midScale = 26.5f;
    public float midAmplitude = 0.05f;

    [Header("Micro Noise")]
    public float microScale = 5f;
    public float microAmplitude = 0.01f;

    private Terrain terrain;
    private Vector2 macroOffset;
    private Vector2 midOffset;
    private Vector2 microOffset;




    [Header("Terrain Layers (assign premade layers in this order)")]
    public TerrainLayer grassLayer;
    public TerrainLayer dirtLayer;
    public TerrainLayer rockLayer;
    public TerrainLayer sandLayer;
    public TerrainLayer snowLayer;

    public float dirtNoiseScale = 1f;
    public float waterHeight = 4f;
    public float snowHeight = 10;
    public float steepnessAmount = .5f;

    [Header("Layer Preferences (0=Grass, 1=Dirt, 2=Rock, 3=Sand, 4=Snow)")]
    public int[] treePreferredLayers = new int[] { 0, 1 };  // trees prefer grass+dirt
    public int[] bushPreferredLayers = new int[] { 1, 0 };  // bushes prefer dirt+grass
    public int[] rockPreferredLayers = new int[] { 2, 3 };  // rocks prefer rock+sand


    [Header("Vegetation Prefabs")]
    public GameObject[] treePrefabs;
    public GameObject[] bushPrefabs;
    public GameObject[] grassPrefabs;
    public GameObject[] rockPrefabs; // for dirt

    public GrassDetail[] grassTextures;



    public int treeDensity = 500;
    public int bushDensity = 1000;
    public int grassDensity = 5000;
    public int rockDensity = 200;


    [Header("Live Editing")]
    public bool live;
    public bool displacement;
    public bool textures;
    public bool vegetation;
    public bool rivers;


    [System.Serializable]
    public struct GrassDetail
    {
        public Texture2D texture;
        public Color healthyColor;
        public Color dryColor;
    }



    void OnValidate()
    {
        if (!live) return;
        terrain = GetComponent<Terrain>();

        if(terrain == null) return;

        if (displacement)
        {
            waterObject.position=new Vector3(0,waterHeight+waterOffset,0);
            GenerateOffsets();
            ApplyDisplacement();
            if (rivers)
            {
                ApplyRivers();
            }

            
        }
        if (textures)
        {
            if (grassLayer != null && dirtLayer != null && rockLayer != null && sandLayer != null && snowLayer != null)
            {
                ApplyTextures();
            }
        }

        if (vegetation)
        {
            ApplyVegetation();
            ApplyGrass();
        }

        


    }
    /* [ContextMenu("Apply Vegetation")]
     public void ApplyVegetation()
     {
         TerrainData td = terrain.terrainData;
         System.Random prng = new System.Random(seed);

         int treeCount = (treePrefabs != null) ? treePrefabs.Length : 0;
         int bushCount = (bushPrefabs != null) ? bushPrefabs.Length : 0;
         int rockCount = (rockPrefabs != null) ? rockPrefabs.Length : 0;

         List<TreePrototype> prototypes = new List<TreePrototype>();
         if (treePrefabs != null) foreach (var p in treePrefabs) prototypes.Add(new TreePrototype { prefab = p });
         if (bushPrefabs != null) foreach (var p in bushPrefabs) prototypes.Add(new TreePrototype { prefab = p });
         if (rockPrefabs != null) foreach (var p in rockPrefabs) prototypes.Add(new TreePrototype { prefab = p });
         td.treePrototypes = prototypes.ToArray();

         List<TreeInstance> newTrees = new List<TreeInstance>();
         // Trees
         for (int i = 0; i < treeDensity; i++)
         {
             float nx = (float)prng.NextDouble();
             float nz = (float)prng.NextDouble();
             float h = td.GetInterpolatedHeight(nx, nz) / td.size.y;
             if (h > waterHeight)
             {
                 newTrees.Add(new TreeInstance
                 {
                     position = new Vector3(nx, h, nz),
                     prototypeIndex = prng.Next(0, treeCount),
                     widthScale = 1f,
                     heightScale = 1f,
                     color = Color.white,
                     lightmapColor = Color.white
                 });
             }
         }

         for (int i = 0; i < bushDensity; i++)
         {
             float nx = (float)prng.NextDouble();
             float nz = (float)prng.NextDouble();
             float h = td.GetInterpolatedHeight(nx, nz) / td.size.y;
             if (h > waterHeight)
             {
                 newTrees.Add(new TreeInstance
                 {
                     position = new Vector3(nx, h, nz),
                     prototypeIndex = prng.Next(0, treeCount),
                     widthScale = 1f,
                     heightScale = 1f,
                     color = Color.white,
                     lightmapColor = Color.white
                 });
             }
         }




         td.treeInstances = newTrees.ToArray();
     }*/



 

    private bool IsOnPreferredLayer(TerrainData td, float nx, float nz, int[] preferredLayers)
    {
        int mapX = Mathf.RoundToInt(nx * (td.alphamapWidth - 1));
        int mapZ = Mathf.RoundToInt(nz * (td.alphamapHeight - 1));

        float[,,] alpha = td.GetAlphamaps(mapX, mapZ, 1, 1);
        for (int i = 0; i < preferredLayers.Length; i++)
        {
            if (alpha[0, 0, preferredLayers[i]] > 0.5f) // at least 50% of this texture
                return true;
        }
        return false;
    }


    [ContextMenu("Apply Grass")]
    public void ApplyGrass()
    {
        TerrainData td = terrain.terrainData;
        if (grassTextures == null || grassTextures.Length == 0) return;

        // Grass detail prototypes
        DetailPrototype[] detailProtos = new DetailPrototype[grassTextures.Length];
        for (int i = 0; i < grassTextures.Length; i++)
        {
            detailProtos[i] = new DetailPrototype
            {
                prototypeTexture = grassTextures[i].texture,
                renderMode = DetailRenderMode.GrassBillboard,
                healthyColor = grassTextures[i].healthyColor,
                dryColor = grassTextures[i].dryColor,
                minWidth = 0.5f,
                maxWidth = 1.5f,
                minHeight = 0.5f,
                maxHeight = 1.5f,
                noiseSpread = 0.3f
            };
        }
        td.detailPrototypes = detailProtos;

        int res = td.detailResolution;

        for (int layer = 0; layer < grassTextures.Length; layer++)
        {
            int[,] detailLayer = new int[res, res];

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    float nx = (float)x / res;
                    float nz = (float)y / res;

                    float h = td.GetInterpolatedHeight(nx, nz) / td.size.y;
                    float steep = td.GetSteepness(nx, nz) / 90f;

                    // get splatmap influence at this point
                    int mapX = Mathf.RoundToInt(nx * (td.alphamapWidth - 1));
                    int mapZ = Mathf.RoundToInt(nz * (td.alphamapHeight - 1));
                    float[,,] alpha = td.GetAlphamaps(mapX, mapZ, 1, 1);

                    float grassWeight = alpha[0, 0, 0]; // grass layer index 0
                    float dirtWeight = alpha[0, 0, 1];  // dirt layer index 1

                    // base density scaled by grass vs dirt
                    float densityFactor = (grassWeight * 1.0f) + (dirtWeight * 0.1f);

                    if (h > waterHeight && steep < 0.5f)
                        detailLayer[y, x] = Mathf.RoundToInt(grassDensity * densityFactor);
                    else
                        detailLayer[y, x] = 0;
                }
            }

            td.SetDetailLayer(0, 0, layer, detailLayer);
        }
    }



    [ContextMenu("Generate Terrain")]
    public void ApplyDisplacement()
    {
        if (terrain == null) return;

        TerrainData terrainData = terrain.terrainData;

        int res = terrainData.heightmapResolution;
        float[,] heights = new float[res, res];

        Vector3 worldPos = terrain.transform.position;
        Vector3 size = terrainData.size;

        for (int x = 0; x < res; x++)
        {
            for (int y = 0; y < res; y++)
            {
                // Convert heightmap pixel to world coordinates
                float worldX = worldPos.x + ((float)x / (res - 1)) * size.x;
                float worldZ = worldPos.z + ((float)y / (res - 1)) * size.z;

                // Macro noise
                float macro = Mathf.PerlinNoise(
                    (worldX + macroOffset.x) / macroScale,
                    (worldZ + macroOffset.y) / macroScale) * macroAmplitude;

                // Mid noise
                float mid = Mathf.PerlinNoise(
                    (worldX + midOffset.x) / midScale,
                    (worldZ + midOffset.y) / midScale) * midAmplitude;

                // Micro noise
                float micro = Mathf.PerlinNoise(
                    (worldX + microOffset.x) / microScale,
                    (worldZ + microOffset.y) / microScale) * microAmplitude;

                float totalHeight = macro + mid + micro+globalOffset;
                heights[y, x] = Mathf.Clamp01(totalHeight);
            }
        }

        terrainData.size = new Vector3(size.x, terrainHeight, size.z);
        terrainData.SetHeights(0, 0, heights);
    }

    [ContextMenu("Apply Textures")]
    public void ApplyTextures()
    {
        TerrainData terrainData = terrain.terrainData;

        // Assign premade layers
        terrainData.terrainLayers = new TerrainLayer[] { grassLayer, dirtLayer, rockLayer, sandLayer, snowLayer };

        int res = terrainData.alphamapResolution;
        float[,,] alphaMaps = new float[res, res, 5];

        Vector3 size = terrainData.size;

        for (int x = 0; x < res; x++)
        {
            for (int y = 0; y < res; y++)
            {
                float normX = (float)x / (res - 1);
                float normY = (float)y / (res - 1);

                float height = terrainData.GetHeight(
                    Mathf.RoundToInt(normX * terrainData.heightmapResolution),
                    Mathf.RoundToInt(normY * terrainData.heightmapResolution)) / size.y;

                float steepness = terrainData.GetSteepness(normX, normY) / 90f;

                float[] weights = new float[5];

                // Grass base
                weights[0] = 1f;

                // Sand below waterline
                if (height < waterHeight)
                    weights[3] = 1f;

                // Dirt patches (randomized noise)
                float dirtNoise = Mathf.PerlinNoise((x + macroOffset.x) * dirtNoiseScale, (y + macroOffset.y) * dirtNoiseScale);
                if (dirtNoise > 0.6f)
                    weights[1] = dirtNoise * 0.5f;

                // Rock on steep slopes
                if (steepness > steepnessAmount)
                {
                    weights[0] = 0;
                    weights[1] = 0;
                    weights[2] = steepness;
                }

                // Snow at high altitude
                if (height > snowHeight)
                    weights[4] = Mathf.InverseLerp(0.7f, 1f, height);

                // Sand below waterline
                if (height < waterHeight)
                {
                    weights[0] = 0;
                    weights[1] = 0;
                    weights[2] = 0;
                    weights[3] = 1f; 
                    weights[4] = 0;
                }

                // Normalize
                float total = 0f;
                for (int i = 0; i < weights.Length; i++) total += weights[i];
                for (int i = 0; i < weights.Length; i++) weights[i] /= total;

                for (int i = 0; i < 5; i++)
                    alphaMaps[y, x, i] = weights[i];
            }
        }

        terrainData.SetAlphamaps(0, 0, alphaMaps);
    }

    [Header("River Settings")]
    public int riverCount = 2;
    public float riverDepth = 0.05f;   // depth in normalized height (0–1)
    public float riverWidth = 0.02f;   // width in normalized map coords
    public int smoothPasses = 3;
    public Vector2 riverFrequency=new Vector2(.5f,1.5f);
    public Vector2 riverAmplitude = new Vector2(5f, 15f);
    public int searchRadius = 5;

    [ContextMenu("Generate Rivers")]
    public void ApplyRivers()
    {
        TerrainData td = terrain.terrainData;
        int res = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, res, res);

        for (int r = 0; r < riverCount; r++)
        {
            // 1. Pick a random start point below water
            Vector2Int start = GetRandomWaterPoint(heights, res,currentRiver:r);

            // 2. Pick another point far away, also below water
            Vector2Int end = GetRandomWaterPoint(heights, res, start, res / 3,currentRiver:r);

            // 3. Carve a line between them
            CarveRiverLine(heights, start, end, riverDepth, riverWidth);
        }

        // 4. Smooth banks
        for (int i = 0; i < smoothPasses; i++)
            heights = SmoothHeights(heights);

        td.SetHeights(0, 0, heights);
    }

    /* private void CarveRiverLine(float[,] heights, Vector2Int start, Vector2Int end, float depth, float width)
  {
      int res = heights.GetLength(0);
      int steps = Mathf.Max(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y));

      // Randomize waviness parameters
      float frequency = Random.Range(riverFrequency.x, riverFrequency.y); // controls how frequent bends are
      float amplitude = Random.Range(riverAmplitude.x, riverAmplitude.y);    // how wide the bends are

          Vector2 dir = (end - start);
          dir.Normalize();
      Vector2 perp = new Vector2(-dir.y, dir.x); // perpendicular vector for side-to-side

      for (int i = 0; i <= steps; i++)
      {
          float t = i / (float)steps;

          // Base position on the straight line
          Vector2 pos = Vector2.Lerp(start, end, t);

          // Add sine-based perpendicular displacement
          float offset = Mathf.Sin(t * Mathf.PI * 2f * frequency) * amplitude;
          pos += perp * offset;

          int x = Mathf.Clamp(Mathf.RoundToInt(pos.x), 0, res - 1);
          int y = Mathf.Clamp(Mathf.RoundToInt(pos.y), 0, res - 1);

          CarveAt(heights, new Vector2Int(x, y), depth, width);
      }
  }*/
    [SerializeField, Range(0f, 1f)]
    private float maxRiverHeight = 0.6f; // clamp rivers to below 60% of terrain height

    private void CarveRiverLine(float[,] heights, Vector2Int start, Vector2Int end, float depth, float width)
    {
        int res = heights.GetLength(0);
        int steps = Mathf.Max(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y));

        float frequency = Random.Range(0.5f, 1.5f);
        float amplitude = Random.Range(5f, 15f);

        Vector2 dir = (end - start);
        dir = dir.normalized;
        Vector2 perp = new Vector2(-dir.y, dir.x);

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;

            // Base position
            Vector2 pos = Vector2.Lerp(start, end, t);

            // Add waviness
            float offset = Mathf.Sin(t * Mathf.PI * 2f * frequency) * amplitude;
            pos += perp * offset;

            int x = Mathf.Clamp(Mathf.RoundToInt(pos.x), 0, res - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(pos.y), 0, res - 1);

            // Only carve if below clamp threshold
            if (heights[y, x] <= maxRiverHeight)
            {
                CarveAt(heights, new Vector2Int(x, y), depth, width);
            }
        }
    }


    private Vector2Int FindLowestNearby(float[,] heights, int cx, int cy, int searchRadius)
    {
        int res = heights.GetLength(0);
        float minHeight = float.MaxValue;
        Vector2Int best = new Vector2Int(cx, cy);

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        {
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                int nx = Mathf.Clamp(cx + dx, 0, res - 1);
                int ny = Mathf.Clamp(cy + dy, 0, res - 1);

                float h = heights[ny, nx];
                if (h < minHeight)
                {
                    minHeight = h;
                    best = new Vector2Int(nx, ny);
                }
            }
        }

        return best;
    }

    int randPoint = 0;

    private Vector2Int GetRandomWaterPoint(float[,] heights, int res, Vector2Int? avoid = null, int minDistance = 0,int currentRiver=0)
    {
        System.Random rand = new System.Random(seed+currentRiver);
       
        int x, y;
        int attempts = 0;
        do
        {
            x=rand.Next(0,res);
           // x = Random.Range(0, res);
            y=rand.Next(0,res);
            //y = Random.Range(0, res);
            randPoint++;
            attempts++;
        }
        while ((heights[y, x] > (waterHeight / terrain.terrainData.size.y) ||
               (avoid.HasValue && Vector2Int.Distance(new Vector2Int(x, y), avoid.Value) < minDistance))
               && attempts < 1000);

        return new Vector2Int(x, y);
    }

    private void CarveAt(float[,] heights, Vector2Int pos, float depth, float width)
    {
        int res = heights.GetLength(0);
        int radius = Mathf.RoundToInt(width * res);

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int nx = pos.x + dx;
                int ny = pos.y + dy;
                if (nx < 0 || ny < 0 || nx >= res || ny >= res) continue;

                float dist = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                if (dist <= 1f)
                {
                    float falloff = Mathf.Cos(dist * Mathf.PI) * 0.5f + 0.5f; // smooth banks
                    heights[ny, nx] -= depth * falloff;
                    heights[ny, nx] = Mathf.Clamp01(heights[ny, nx]);
                }
            }
        }
    }

    private float[,] SmoothHeights(float[,] heights)
    {
        int res = heights.GetLength(0);
        float[,] newHeights = new float[res, res];

        for (int x = 1; x < res - 1; x++)
        {
            for (int y = 1; y < res - 1; y++)
            {
                float avg = 0f;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        avg += heights[y + dy, x + dx];

                newHeights[y, x] = avg / 9f;
            }
        }
        return newHeights;
    }





    [ContextMenu("Apply Vegetation")]
    public void ApplyVegetation()
    {
        if (terrain == null) terrain = GetComponent<Terrain>();
        TerrainData td = terrain.terrainData;
        System.Random prng = new System.Random(seed);

        List<TreePrototype> prototypes = new List<TreePrototype>();
        int treeStart = 0;
        int bushStart = 0;
        int rockStart = 0;

        // Add trees
        if (treePrefabs != null)
        {
            treeStart = prototypes.Count;
            foreach (var p in treePrefabs) prototypes.Add(new TreePrototype { prefab = p });
        }

        // Add bushes
        if (bushPrefabs != null)
        {
            bushStart = prototypes.Count;
            foreach (var p in bushPrefabs) prototypes.Add(new TreePrototype { prefab = p });
        }

        // Add rocks
        if (rockPrefabs != null)
        {
            rockStart = prototypes.Count;
            foreach (var p in rockPrefabs) prototypes.Add(new TreePrototype { prefab = p });
        }

        td.treePrototypes = prototypes.ToArray();

        List<TreeInstance> newTrees = new List<TreeInstance>();

        // --- Spawn Trees ---
        for (int i = 0; i < treeDensity; i++)
        {
            Random.InitState(seed+i);
            float randomScale = Random.Range(.5f, 1f);
            float nx = (float)prng.NextDouble();
            float nz = (float)prng.NextDouble();
            float h = td.GetInterpolatedHeight(nx, nz) / td.size.y;

            if (h > waterHeight && IsOnPreferredLayer(td, nx, nz, treePreferredLayers))
            {
                newTrees.Add(new TreeInstance
                {
                    position = new Vector3(nx, h, nz),
                    prototypeIndex = prng.Next(treeStart, treeStart + (treePrefabs?.Length ?? 0)),
                    rotation=Random.Range(0,360f),
                    widthScale = randomScale,
                    heightScale = randomScale,
                    color = Color.white,
                    lightmapColor = Color.white
                });
                
            }
        }

        // --- Spawn Bushes ---
        for (int i = 0; i < bushDensity; i++)
        {
            Random.InitState(seed + i);
            float randomScale = Random.Range(.5f, 1f);
            float nx = (float)prng.NextDouble();
            float nz = (float)prng.NextDouble();
            float h = td.GetInterpolatedHeight(nx, nz) / td.size.y;

            if (h > waterHeight && IsOnPreferredLayer(td, nx, nz, bushPreferredLayers))
            {
                newTrees.Add(new TreeInstance
                {
                    position = new Vector3(nx, h, nz),
                    prototypeIndex = prng.Next(bushStart, bushStart + (bushPrefabs?.Length ?? 0)),
                    rotation = Random.Range(0, 360f),
                    widthScale =randomScale,
                    heightScale = randomScale,
                    color = Color.white,
                    lightmapColor = Color.white
                });
            }
        }

        // --- Spawn Rocks ---
        for (int i = 0; i < rockDensity; i++)
        {
            Random.InitState(seed + i);
            float randomScale = Random.Range(.5f, 1f);
            float nx = (float)prng.NextDouble();
            float nz = (float)prng.NextDouble();
            float h = td.GetInterpolatedHeight(nx, nz) / td.size.y;

            if (h > waterHeight && h < snowHeight && IsOnPreferredLayer(td, nx, nz, rockPreferredLayers))
            {
                newTrees.Add(new TreeInstance
                {
                    position = new Vector3(nx, h, nz),
                    prototypeIndex = prng.Next(rockStart, rockStart + (rockPrefabs?.Length ?? 0)),
                    rotation = Random.Range(0, 360f),
                    widthScale = randomScale,
                    heightScale = randomScale,
                    color = Color.white,
                    lightmapColor = Color.white
                });
            }
        }

        td.treeInstances = newTrees.ToArray();
    }



    [ContextMenu("Randomize Seed")]
    public void RandomizeSeed()
    {
        seed = Random.Range(int.MinValue, int.MaxValue);
        GenerateOffsets();
        ApplyDisplacement();
    }

    private void GenerateOffsets()
    {
        System.Random prng = new System.Random(seed);

        macroOffset = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));
        midOffset = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));
        microOffset = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));
    }
}

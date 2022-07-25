using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Physics.Systems;
using Unity.Rendering;

namespace RuntimeGI
{
    [UnityEngine.ExecuteInEditMode]
    public class RuntimeGIManager : UnityEngine.MonoBehaviour, IConvertGameObjectToEntity
    {
        public bool UpdateGI;
        public RuntimeGISettings Settings;
        public UnityEngine.Material DefaultMaterial;

        public float BounceExp;
        public float BounceWeight;
        public float LightStrengthNormalizer;
        public float MaxLightSpereSize;

        public bool StepUpdateGI { get; set; }
        public bool UpdateRays { get; set; }
        public bool UpdateLights { get; set; }
        public bool UpdateLightmaps { get; set; }

        private World _GIWorld;
        private RuntimeGISystem _RuntimeGISystem;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            _RuntimeGISystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<RuntimeGISystem>();
            _RuntimeGISystem.Settings = Settings;
            _RuntimeGISystem.DefaultMaterial = DefaultMaterial;
        }

        public bool HasWorld()
        {
            return _GIWorld != null;
        }

        public void SetupGIWorld()
        {
            if (_GIWorld != null)
                _GIWorld.Dispose();

            _GIWorld = new World("GIWorld", WorldFlags.Editor);
            var group = _GIWorld.GetOrCreateSystem<SimulationSystemGroup>();
            var buildPhys = _GIWorld.GetOrCreateSystem<BuildPhysicsWorld>();
            _RuntimeGISystem = _GIWorld.GetOrCreateSystem<RuntimeGISystem>();
            _RuntimeGISystem.Settings = Settings;
            _RuntimeGISystem.DefaultMaterial = DefaultMaterial;

            group.AddSystemToUpdateList(buildPhys);
            group.AddSystemToUpdateList(_RuntimeGISystem);
            group.SortSystems();

            var conversion = _GIWorld.GetOrCreateSystem<ConvertToEntitySystem>();
            World.DefaultGameObjectInjectionWorld = _GIWorld;

            var toConvert = FindObjectsOfType<ConvertToEntity>();
            for (int i = 0; i < toConvert.Length; i++)
            {
                conversion.AddToBeConverted(World.DefaultGameObjectInjectionWorld, toConvert[i]);
            }

            conversion.Update();
        }

        public void DisposeWorld()
        {
            _GIWorld.Dispose();
            _GIWorld = null;
        }

        public void Update()
        {
            if (_GIWorld != null)
            {
                if (UpdateGI)
                {
                    _RuntimeGISystem.Settings = Settings;
                    //_RuntimeGISystem.GenerateRays();
                    _RuntimeGISystem.UpdateLights();
                    _GIWorld.Update();
                }
                else
                {
                    if (StepUpdateGI)
                    {
                        StepUpdateGI = false;
                        _RuntimeGISystem.Settings = Settings;
                        _RuntimeGISystem.UpdateLights();
                        _GIWorld.Update();
                    }

                    if (UpdateRays)
                    {
                        UpdateRays = false;
                        _RuntimeGISystem.Settings = Settings;
                    }

                    if (UpdateLights)
                    {
                        UpdateLights = false;
                        _RuntimeGISystem.UpdateLights();
                    }

                    if (UpdateLightmaps)
                    {
                        UpdateLightmaps = false;
                        _RuntimeGISystem.UpdateLightmaps();
                    }
                }
            }
        }

        public void StepWorld()
        {
            StepUpdateGI = true;
        }

        public void CreateTextures()
        {
            _RuntimeGISystem.UpdateLightmaps();
        }

        private void OnDrawGizmosSelected()
        {
            if (_GIWorld == null)
                return;

            using (var query = _GIWorld.EntityManager.CreateEntityQuery(typeof(LitPointData)))
            {
                var points = query.ToComponentDataArray<LitPointData>(Allocator.Temp);

                for (int i = 0; i < points.Length; i++)
                {
                    var bounceExp = math.pow(math.length(points[i].AccLight), BounceExp);

                    UnityEngine.Gizmos.color = (UnityEngine.Vector4)(points[i].AccLight * BounceWeight * bounceExp) / LightStrengthNormalizer;
                    UnityEngine.Gizmos.color = new UnityEngine.Color(UnityEngine.Gizmos.color.r, UnityEngine.Gizmos.color.g, UnityEngine.Gizmos.color.b, 1);
                    UnityEngine.Gizmos.DrawSphere(points[i].Position, MaxLightSpereSize);
                }
            }
        }
    }

    public struct SettingUpLitPoint : IComponentData { }
    public struct LitPointData : IComponentData
    {
        public float4 AccLight;
        public quaternion Rotation;
        public float3 Position;
        public float3 Albedo;
        public float3 Normal => math.mul(Rotation, math.up());
    }

    public struct LitPoint
    {
        public quaternion Rotation;
        public float3 Position;

        public float3 Normal => math.mul(Rotation, math.up());
    }

    [System.Serializable]
    public struct RuntimeGISettings
    {
        public float MaxRaycastLength;

        public bool UseSphereAreaFalloff;
        public bool UsePointLightRadius;
        public bool ExtraProcessCorners;
        public float CornerThresholdSqrd;

        public float TexturePadding;

        public bool UseFibonacciFan;
        public int RaycastCountLow;
        public int RaycastCountMid;
        public int RaycastCountHigh;

        public float StartSpreadAngle;
        public int CircleCount;
        public int SpreadCount;
    }

    public struct RuntimeGILightmap : ISharedComponentData, System.IEquatable<RuntimeGILightmap>
    {
        public UnityEngine.Material Material;

        public bool Equals(RuntimeGILightmap other)
        {
            return Material == other.Material;
        }

        public override int GetHashCode()
        {
            if (Material != null)
                return Material.GetHashCode();

            return base.GetHashCode();
        }
    }

    [System.Serializable]
    public struct RuntimeGIMaterial : IComponentData
    {
        public int2 Resolution;
        public UnityEngine.Color Albedo;
        public float Smoothness;
        public float Metallic;

        public int TotalPoints => Resolution.x * Resolution.y;
        public float3 AlbedoF3 => new float3(Albedo.r, Albedo.g, Albedo.b);
        public float4 AlbedoF4 => new float4(Albedo.r, Albedo.g, Albedo.b, Albedo.a);
    }

    public struct RuntimeGIShapeState : IComponentData
    {
        public float CurrentFanRotation;
        public int CurrentFadeIn;
        public byte SubShapeIndex;
        //public byte FidelityStage;
        public bool Dirt;
    }

    public struct RuntimeGILitPoints : IBufferElementData
    {
        public Entity Entity;
    }

    public struct LitPolygon : IBufferElementData
    {
        public float2 LB;
        public float2 LT;
        public float2 RT;
        public float2 RB;

        public UnityEngine.Rect Rect;

        public int Start;
        public int Length;
        public int Width;
        public int Height;

        public void GetBoundingBox(out float4 bounds, out float2 size)
        {
            float left = 1f, down = 1f;
            float right = 0f, up = 0f;

            if (LB.x < left) left = LB.x;
            if (LT.x < left) left = LT.x;
            if (RT.x < left) left = RT.x;
            if (RB.x < left) left = RB.x;

            if (LB.x > right) right = LB.x;
            if (LT.x > right) right = LT.x;
            if (RT.x > right) right = RT.x;
            if (RB.x > right) right = RB.x;

            if (LB.y < down) down = LB.y;
            if (LT.y < down) down = LT.y;
            if (RT.y < down) down = RT.y;
            if (RB.y < down) down = RB.y;

            if (LB.y > up) up = LB.y;
            if (LT.y > up) up = LT.y;
            if (RT.y > up) up = RT.y;
            if (RB.y > up) up = RB.y;

            bounds = new float4(left, right, down, up);
            size = new float2(right - left, up - down);
        }

        public void RasterizeSquare(NativeSlice<float3> litPixels, NativeArray<UnityEngine.Color32> texture, int texWidth, int texHeight)
        {
            var imgLeft = (int)(Rect.xMin * texWidth);
            var imgBot = (int)(Rect.yMin * texHeight);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var texCoordX = imgLeft + (Width - x);
                    var texCoordY = imgBot + (Height - y);

                    var litPixelIndex = (y * Width) + x;
                    var textureIndex = (texCoordY * texWidth) + texCoordX;
                    var normColor = litPixels[litPixelIndex] * RuntimeGISystem.LIGHT_STRENGTH_MUL;

                    texture[textureIndex] = new UnityEngine.Color32((byte)(normColor.x * byte.MaxValue),
                                                                    (byte)(normColor.y * byte.MaxValue),
                                                                    (byte)(normColor.z * byte.MaxValue), byte.MaxValue);
                }
            }
        }

        public void Rasterize(NativeSlice<float3> litPixels, NativeArray<float4> texturePixels)
        {
            //  public-domain code by Darel Rex Finley, 2007
            int polyCorners = 4;
            int[] nodeX = new int[polyCorners];

            int nodes, pixelY, i, j, swap;

            GetBoundingBox(out var bounds, out var size);

            int imgLeft = (int)(bounds.x * Width);
            int imgRight = (int)(bounds.y * Width);
            int imgBot = (int)(bounds.z * Height);
            int imgTop = (int)(bounds.w * Height);


            float[] polyX = { LB.x, LT.x, RT.x, RB.x };
            float[] polyY = { LB.y, LT.y, RT.y, RB.y };

            //  Loop through the rows of the image.
            for (pixelY = imgTop; pixelY < imgBot; pixelY++)
            {
                //  Build a list of nodes.
                nodes = 0;
                j = polyCorners - 1;
                for (i = 0; i < polyCorners; i++)
                {
                    if (polyY[i] < pixelY && polyY[j] >= pixelY || polyY[j] < pixelY && polyY[i] >= pixelY)
                    {
                        nodeX[nodes++] = (int)(polyX[i] + (pixelY - polyY[i]) / (polyY[j] - polyY[i]) * (polyX[j] - polyX[i]));
                    }
                    j = i;
                }

                //  Sort the nodes, via a simple “Bubble” sort.
                i = 0;
                while (i < nodes - 1)
                {
                    if (nodeX[i] > nodeX[i + 1])
                    {
                        swap = nodeX[i];
                        nodeX[i] = nodeX[i + 1];
                        nodeX[i + 1] = swap;
                        if (i != 0)
                            i--;
                    }
                    else
                    {
                        i++;
                    }
                }

                //  Fill the pixels between node pairs.
                for (i = 0; i < nodes; i += 2)
                {
                    if (nodeX[i] >= imgRight)
                        break;

                    if (nodeX[i + 1] > imgLeft)
                    {
                        if (nodeX[i] < imgLeft)
                            nodeX[i] = imgLeft;

                        if (nodeX[i + 1] > imgRight)
                            nodeX[i + 1] = imgRight;

                        for (int pixelX = nodeX[i]; pixelX < nodeX[i + 1]; pixelX++)
                        {
                            int index = (pixelY * Width) + pixelX;
                            texturePixels[index] = new float4(litPixels[index], 1);
                        }
                    }
                }
            }
        }
    }

    public struct LightData
    {
        public float4 ColorAndStrength;
        public float3 Position;
        public float3 Direction;
        public float3 ConeAnglesAndRadius;

        public float3 Color => ColorAndStrength.xyz;
        public float Strength => ColorAndStrength.w;
        public float OuterAngle => ConeAnglesAndRadius.x;
        public float InnerAngle => ConeAnglesAndRadius.y;
        public float Radius => ConeAnglesAndRadius.z;

        public struct Comparer : IComparer<(float, LightData)>
        {
            public int Compare((float, LightData) x, (float, LightData) y)
            {
                return x.Item1.CompareTo(y.Item1);
            }
        }
    }

    public struct RaycastFans
    {
        public BlobAssetReference<Fans> Value;

        public static RaycastFans Setup(RuntimeGISettings settings)
        {
            var data = new RaycastFans();

            using (var builder = new BlobBuilder(Allocator.TempJob))
            {
                ref var root = ref builder.ConstructRoot<Fans>();

                if (settings.UseFibonacciFan)
                {
                    var low = builder.Allocate(ref root.Low, settings.RaycastCountLow);
                    var mid = builder.Allocate(ref root.Mid, settings.RaycastCountMid);
                    var high = builder.Allocate(ref root.High, settings.RaycastCountHigh);

                    HalfFibonacciSphere(ref low);
                    HalfFibonacciSphere(ref mid);
                    HalfFibonacciSphere(ref high);
                }
                else
                {
                    var fan = SimpleSphereFan(settings);

                    var low = builder.Allocate(ref root.Low, fan.Count);
                    var mid = builder.Allocate(ref root.Mid, fan.Count);
                    var high = builder.Allocate(ref root.High, fan.Count);

                    for (int i = 0; i < low.Length; i++)
                        low[i] = fan[i];
                    for (int i = 0; i < mid.Length; i++)
                        mid[i] = fan[i];
                    for (int i = 0; i < high.Length; i++)
                        high[i] = fan[i];

                    UnityEngine.Debug.Log($"fan.Count: {fan.Count}");
                }

                data.Value = builder.CreateBlobAssetReference<Fans>(Allocator.Persistent);
            }

            return data;
        }

        static List<quaternion> SimpleSphereFan(RuntimeGISettings settings)
        {
            var rayRotations = new List<quaternion>();
            var startSpreadAngleRad = math.radians(settings.StartSpreadAngle);
            var spreadRange = (math.PI / 2f) - startSpreadAngleRad;
            for (int c = 0; c < settings.CircleCount; c++)
            {
                var circleStep = (c / (float)settings.CircleCount) * math.PI * 2;
                var circleRot = quaternion.AxisAngle(math.up(), circleStep);

                for (int s = 0; s < settings.SpreadCount; s++)
                {
                    var spreadStep = ((s / (float)settings.SpreadCount) * spreadRange) + startSpreadAngleRad;
                    var spreadRot = quaternion.AxisAngle(math.right(), (math.PI / 2f) - spreadStep);
                    rayRotations.Add(math.mul(circleRot, spreadRot));
                }
            }

            return rayRotations;
        }

        static void HalfFibonacciSphere(ref BlobBuilderArray<quaternion> points)
        {
            var goldenRatio = (1 + math.sqrt(5)) / 2f;
            var angleIncrement = math.PI * 2f * goldenRatio;

            var straight = math.up();
            for (int i = 0; i < points.Length; i++)
            {
                var t = (float)i / points.Length;
                var angle1 = math.acos(1 - (2 * t));
                var angle2 = angleIncrement * i;

                var x = math.sin(angle1) * math.cos(angle2);
                var y = math.sin(angle1) * math.sin(angle2);
                var z = math.cos(angle1);

                var dir = new float3(x, y, z);
                if (math.dot(straight, dir) < 0)
                    dir = -dir;

                var up = math.forward();
                if (dir.Equals(up))
                    up = math.up();

                points[i] = LookRotationExtended(dir, up, math.up(), math.forward());
            }
        }

        static quaternion LookRotationExtended(float3 alignWithVector, float3 alignWithNormal, float3 customForward, float3 customUp)
        {
            quaternion rotationA = quaternion.LookRotation(alignWithVector, alignWithNormal);
            quaternion rotationB = quaternion.LookRotation(customForward, customUp);

            return math.normalize(math.mul(rotationA, math.inverse(rotationB)));
        }
    }

    public struct Fans
    {
        public BlobArray<quaternion> Low;
        public BlobArray<quaternion> Mid;
        public BlobArray<quaternion> High;
    }

    [AlwaysUpdateSystem]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    partial class RuntimeGISystem : SystemBase
    {
        public const float LIGHT_STRENGTH_MUL = 1f;
        const float MATERIAL_EDGE_PADDING = 0.001f;
        const int LIGHTMAP_SIZE = 256;// 2048;
        const int LIGHTS_PER_POINT = 3;


        class LightmapSetupData
        {
            public int LightmapIndex;
            public List<Entity> Entities;
            public UnityEngine.Rect[] TextureST;
        }

        public Material SourceMat;
        public Material MaterialInstance;

        //
        public RuntimeGISettings Settings;
        public UnityEngine.Material DefaultMaterial;

        private BuildPhysicsWorld _BuildPhysicsWorld;
        private BeginInitializationEntityCommandBufferSystem _ECBSystem;

        private NativeArray<quaternion> _SmallRaySet;
        private NativeArray<quaternion> _FullRaySet;
        private RaycastFans _RaycastFans;

        private NativeArray<LightData> _LightData;

        private Stopwatch _StopWatch;

        private List<(Entity, RuntimeGIShape)> _QueuedShapes = new List<(Entity, RuntimeGIShape)>();
        private NativeArray<LitPoint> _LitPoints;
        private NativeArray<float3> _LitPixels;

        protected override void OnCreate()
        {
            _BuildPhysicsWorld = World.GetExistingSystem<BuildPhysicsWorld>();
            _ECBSystem = World.GetExistingSystem<BeginInitializationEntityCommandBufferSystem>();

            _StopWatch = new Stopwatch();
        }

        protected override void OnStartRunning()
        {
            UpdateLights();
            _RaycastFans = RaycastFans.Setup(Settings);

            var totalPoints = 0;
            for (int i = 0; i < _QueuedShapes.Count; i++)
            {
                // THIS DOES NOT WORK REALLY
                totalPoints += _QueuedShapes[i].Item2.Material.TotalPoints;
            }

            _LitPoints = new NativeArray<LitPoint>(totalPoints, Allocator.Persistent);
            _LitPixels = new NativeArray<float3>(totalPoints, Allocator.Persistent);

            var texPackingMap = new Stack<(int, UnityEngine.Texture2D[])>();

            var currentStartIndex = 0;
            for (int i = 0; i < _QueuedShapes.Count; i++)
            {
                var entity = _QueuedShapes[i].Item1;
                var shape = _QueuedShapes[i].Item2;
                var slice = new NativeSlice<LitPoint>(_LitPoints, currentStartIndex, shape.Material.TotalPoints);
                var pointRanges = new NativeList<LitPolygon>(Allocator.TempJob);

                RuntimeGIShape.GenerateLitPointsFromShape(slice, currentStartIndex, pointRanges, shape.GetComponent<PhysicsShapeAuthoring>(), shape.Material.Resolution, MATERIAL_EDGE_PADDING);

                var subTextures = new UnityEngine.Texture2D[pointRanges.Length];
                for (int k = 0; k < pointRanges.Length; k++)
                {
                    subTextures[k] = new UnityEngine.Texture2D(pointRanges[k].Width, pointRanges[k].Height, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
                }

                texPackingMap.Push((i, subTextures));

                EntityManager.AddBuffer<LitPolygon>(entity).AddRange(pointRanges);
                currentStartIndex += shape.Material.TotalPoints;
            }

            UnityEngine.Texture2D packingTex = null;
            UnityEngine.Rect[] packedRects = null;
            List<UnityEngine.Texture2D> prevContTexutres = null;

            var needNewTexture = true;
            var packedEntities = new List<Entity>();
            var lightmapIndexData = new List<LightmapSetupData>();
            var lightmaps = new List<UnityEngine.Texture2D>();
            var contTexutres = new List<UnityEngine.Texture2D>();
            while (texPackingMap.Count > 0)
            {
                var data = texPackingMap.Peek();

                if (needNewTexture)
                {
                    packedEntities.Clear();
                    packedEntities.Add(_QueuedShapes[data.Item1].Item1);

                    packingTex = new UnityEngine.Texture2D(2, 2, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
                    lightmaps.Add(packingTex);
                    contTexutres.AddRange(data.Item2);
                }
                else
                {
                    prevContTexutres = new List<UnityEngine.Texture2D>(contTexutres);
                    contTexutres.AddRange(data.Item2);
                    packedEntities.Add(_QueuedShapes[data.Item1].Item1);
                    packedRects = packingTex.PackTextures(contTexutres.ToArray(), 1, LIGHTMAP_SIZE);
                    needNewTexture = packedRects == null;
                }

                if (needNewTexture == false)
                {
                    for (int i = 0; i < packedRects.Length; i++)
                    {
                        var packedWidth = packedRects[i].width * packingTex.width;
                        var packedHeight = packedRects[i].height * packingTex.height;

                        if (packedWidth < contTexutres[i].width || packedHeight < contTexutres[i].height)
                        {
                            needNewTexture = true;
                            break;
                        }
                    }

                    if (needNewTexture || texPackingMap.Count == 1)
                    {
                        if (needNewTexture)
                            packedEntities.RemoveAt(packedEntities.Count - 1);

                        if (needNewTexture)
                            packedRects = packingTex.PackTextures(prevContTexutres.ToArray(), 2, LIGHTMAP_SIZE);
                        else
                            packedRects = packingTex.PackTextures(contTexutres.ToArray(), 2, LIGHTMAP_SIZE);

                        var polygonIndex = 0;
                        var lastEntity = Entity.Null;
                        for (int i = 0; i < packedEntities.Count; i++)
                        {
                            if (lastEntity == packedEntities[i])
                                polygonIndex++;
                            else
                                polygonIndex = 0;

                            float packingSizeRatioX = packingTex.width / (float)LIGHTMAP_SIZE;
                            float packingSizeRatioY = packingTex.height / (float)LIGHTMAP_SIZE;

                            var rect = packedRects[i];

                            rect.x *= packingSizeRatioX;
                            rect.y *= packingSizeRatioX;
                            rect.width *= packingSizeRatioY;
                            rect.height *= packingSizeRatioY;

                            var polygons = EntityManager.GetBuffer<LitPolygon>(packedEntities[i]);
                            var polygon = polygons[polygonIndex];
                            polygon.Rect = rect;
                            polygons[polygonIndex] = polygon;

                            var resAdjustedPadding = Settings.TexturePadding / new float2(packingTex.width, packingTex.height);

                            EntityManager.AddComponentData(packedEntities[i], new BuiltinMaterialPropertyUnity_LightmapST
                            {
                                Value = new float4(rect.width - (resAdjustedPadding.x * 2), rect.height - (resAdjustedPadding.y * 2), rect.x + resAdjustedPadding.x, rect.y + resAdjustedPadding.y)
                            });
                            EntityManager.AddComponentData(packedEntities[i], new BuiltinMaterialPropertyUnity_LightmapIndex
                            {
                                Value = new float4(lightmaps.Count - 1)
                            });

                            lastEntity = packedEntities[i];
                        }

                        lightmapIndexData.Add(new LightmapSetupData
                        {
                            LightmapIndex = lightmaps.Count - 1,
                            Entities = packedEntities,
                        });
                    }
                }
                else
                {
                    needNewTexture = false;
                }

                if (needNewTexture == false)
                {
                    texPackingMap.Pop();
                }
            }

            for (int i = 0; i < lightmaps.Count; i++)
            {
                var correctTex = new UnityEngine.Texture2D(LIGHTMAP_SIZE, LIGHTMAP_SIZE, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain);
                UnityEngine.Graphics.CopyTexture(lightmaps[i], 0, 0, 0, 0, lightmaps[i].width, lightmaps[i].height, correctTex, 0, 0, 0, 0);
                UnityEngine.GameObject.DestroyImmediate(lightmaps[i]);
                lightmaps[i] = correctTex;
            }

            var lightmapArray = LightMaps.ConstructLightMaps(lightmaps, null, null);

            for (int k = 0; k < lightmapIndexData.Count; k++)
            {
                for (int i = 0; i < lightmapIndexData[k].Entities.Count; i++)
                {
                    EntityManager.AddSharedComponentData(lightmapIndexData[k].Entities[i], lightmapArray);
                    var renderMesh = EntityManager.GetSharedComponentData<RenderMesh>(lightmapIndexData[k].Entities[i]);

                    var lightMappedMaterial = new UnityEngine.Material(renderMesh.material);
                    lightMappedMaterial.name = $"{lightMappedMaterial.name}_Lightmapped_";
                    lightMappedMaterial.EnableKeyword("LIGHTMAP_ON");

                    lightMappedMaterial.SetTexture("unity_Lightmaps", lightmapArray.colors);
                    renderMesh.material = lightMappedMaterial;

                    EntityManager.SetSharedComponentData(lightmapIndexData[k].Entities[i], renderMesh);
                }
            }

            for (int i = 0; i < _QueuedShapes.Count; i++)
            {
                _QueuedShapes[i].Item2.gameObject.SetActive(false);
            }

            PhysicsRuntimeExtensions.RegisterPhysicsRuntimeSystemReadOnly(this);
        }

        public void AddShape(Entity entity, RuntimeGIShape shape)
        {
            _QueuedShapes.Add((entity, shape));
        }

        public void UpdateLights()
        {
            var lightsDirty = false;
            var lights = UnityEngine.GameObject.FindObjectsOfType<RuntimeGILight>();

            if (_LightData.IsCreated == false || _LightData.Length != lights.Length)
            {
                if (_LightData.IsCreated)
                    _LightData.Dispose();

                _LightData = new NativeArray<LightData>(lights.Length, Allocator.Persistent);
                lightsDirty = true;
            }

            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i].GetComponent<UnityEngine.Light>();
                var hdLightData = lights[i].GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                var lightColor = UnityEngine.Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);

                var innerAngle = (hdLightData.innerSpotPercent / 100) * light.spotAngle;

                var lightData = new LightData
                {
                    Position = lights[i].transform.position,
                    Direction = lights[i].transform.forward,
                    ColorAndStrength = new float4(lightColor.r, lightColor.g, lightColor.b, hdLightData.intensity),
                    ConeAnglesAndRadius = new float3(math.radians(light.spotAngle / 2f), math.radians(innerAngle / 2f), hdLightData.shapeRadius),
                };

                if (_LightData[i].Equals(lightData) == false)
                    lightsDirty = true;

                _LightData[i] = lightData;
            }

            if (lightsDirty)
            {
                Entities.ForEach((ref RuntimeGIShapeState shapeState) =>
                {
                    shapeState.CurrentFadeIn = 0;

                }).Run();
            }
        }

        protected override void OnUpdate()
        {
            UpdateLightmaps();

            UpdateLights();

            Dependency = new RaycastLitPointsJob
            {
                Settings = Settings,
                RaycastFans = _RaycastFans,
                CollisionWorld = _BuildPhysicsWorld.PhysicsWorld.CollisionWorld,
                LightComparer = new LightData.Comparer(),

                LightData = _LightData,

                RuntimeGIMaterials = GetComponentDataFromEntity<RuntimeGIMaterial>(true),

                AllLitPoints = _LitPoints,
                AllLitPixels = _LitPixels,

            }.ScheduleParallel(Dependency); //.Run();//

            _ECBSystem.AddJobHandleForProducer(Dependency);
        }

        public void UpdateLightmaps()
        {
            using (var query = EntityManager.CreateEntityQuery(typeof(LitPolygon)))
            {
                var entities = query.ToEntityArray(Allocator.Temp);

                var lightmaps = (LightMaps?)null;
                for (int k = 0; k < entities.Length; k++)
                {
                    var state = EntityManager.GetComponentData<RuntimeGIShapeState>(entities[k]);
                    if (state.Dirt)
                    {
                        var lightmapIndex = EntityManager.GetComponentData<BuiltinMaterialPropertyUnity_LightmapIndex>(entities[k]);
                        var litPolygons = EntityManager.GetBuffer<LitPolygon>(entities[k]);

                        lightmaps = EntityManager.GetSharedComponentData<LightMaps>(entities[k]);
                        var lightmap = lightmaps.Value.colors.GetPixelData<UnityEngine.Color32>(0, (int)lightmapIndex.Value.x);

                        for (int i = 0; i < litPolygons.Length; i++)
                        {
                            var slice = new NativeSlice<float3>(_LitPixels, litPolygons[i].Start, litPolygons[i].Length);
                            litPolygons[i].RasterizeSquare(slice, lightmap, lightmaps.Value.colors.width, lightmaps.Value.colors.height);
                        }
                    }
                }

                if (lightmaps.HasValue)
                {
                    UnityEngine.Debug.Log("Texture.Apply");
                    lightmaps.Value.colors.Apply();
                }
                for (int k = 0; k < entities.Length; k++)
                {
                    var state = EntityManager.GetComponentData<RuntimeGIShapeState>(entities[k]);
                    if (state.Dirt)
                    {
                        state.Dirt = false;
                        EntityManager.SetComponentData(entities[k], state);
                    }
                }
            }
        }

        [BurstCompile]
        partial struct RaycastLitPointsJob : IJobEntity
        {
            [ReadOnly] public RuntimeGISettings Settings;
            [ReadOnly] public RaycastFans RaycastFans;
            [ReadOnly] public CollisionWorld CollisionWorld;
            [ReadOnly] public LightData.Comparer LightComparer;
            [ReadOnly] public NativeArray<LightData> LightData;
            [ReadOnly] public ComponentDataFromEntity<RuntimeGIMaterial> RuntimeGIMaterials;
            [ReadOnly] public NativeArray<LitPoint> AllLitPoints;

            [NativeDisableParallelForRestriction]
            public NativeArray<float3> AllLitPixels;

            public void Execute(ref RuntimeGIShapeState state, in RuntimeGIMaterial material, in DynamicBuffer<LitPolygon> pointRanges)
            {
                var currentRange = pointRanges[state.SubShapeIndex];
                state.SubShapeIndex = (byte)((state.SubShapeIndex + 1) % pointRanges.Length);

                var litPoints = new NativeSlice<LitPoint>(AllLitPoints, currentRange.Start, currentRange.Length);
                var litPixels = new NativeSlice<float3>(AllLitPixels, currentRange.Start, currentRange.Length);

                //var fidelityStage = state.FidelityStage;
                //switch (state.FidelityStage)
                //{
                //    case 0:
                //        state.Dirt = true;
                //        state.FidelityStage++;
                //        break;
                //    case 1:
                //        state.Dirt = true;
                //        state.FidelityStage++;
                //        break;
                //    case 2:
                //        state.Dirt = true;
                //        state.CurrentFanRotation += 0.01f;
                //        //state.FidelityStage++;
                //        break;
                //    default:
                //        return;
                //}

                if (state.CurrentFadeIn > 1000)
                    return;
                else
                {
                    state.CurrentFanRotation += 0.01f;
                    state.CurrentFadeIn++;
                    if ((state.CurrentFadeIn % 10) == 1)
                        state.Dirt = true;
                }

                var closestLights = new NativeList<(float, LightData)>(LightData.Length, Allocator.Temp);

                for (int l = 0; l < LightData.Length; l++)
                {
                    var distSqrd = math.distancesq(litPoints[litPoints.Length / 2].Position, LightData[l].Position);
                    closestLights.Add((distSqrd, LightData[l]));
                }

                closestLights.Sort(LightComparer);

                var fanRotOffset = quaternion.AxisAngle(math.up(), state.CurrentFanRotation % (math.PI * 2));

                for (int i = 0; i < litPoints.Length; i++)
                {
                    var data = litPoints[i];
                    var accLight = (float3)default;
                    var hits = 0;
                    var rotatedNormal = math.mul(data.Rotation, fanRotOffset);

                    for (int r = 0; r < RaycastFans.Value.Value.Low.Length; r++)
                    {
                        DoRaycast(ref hits, ref accLight, data.Position, rotatedNormal, data.Normal, RaycastFans.Value.Value.High[r], closestLights);
                    }

                    //switch (fidelityStage)
                    //{
                    //    case 0:
                    //        //litPixels[i] = default;
                    //        for (int r = 0; r < RaycastFans.Value.Value.Low.Length; r++)
                    //        {
                    //            DoRaycast(/*ref data, */ref hits, ref accLight, data.Position, rotatedNormal, data.Normal, RaycastFans.Value.Value.Low[r], closestLights);
                    //        }
                    //        break;
                    //    case 1:
                    //        for (int r = 0; r < RaycastFans.Value.Value.Mid.Length; r++)
                    //        {
                    //            DoRaycast(/*ref data, */ref hits, ref accLight, data.Position, rotatedNormal, data.Normal, RaycastFans.Value.Value.Mid[r], closestLights);
                    //        }
                    //        break;
                    //    case 2:
                    //        for (int r = 0; r < RaycastFans.Value.Value.High.Length; r++)
                    //        {
                    //            DoRaycast(/*ref data, */ref hits, ref accLight, data.Position, rotatedNormal, data.Normal, RaycastFans.Value.Value.High[r], closestLights);
                    //        }
                    //        break;
                    //    default:
                    //        break;
                    //}

                    var ambient = new float3(0.03f) * material.AlbedoF3;
                    accLight = ambient + accLight;

                    //if (fidelityStage == 0)
                    //    litPixels[i] = PBR.GammaCorrect(accLight / hits);
                    //else

                    if (state.CurrentFadeIn == 1)
                        litPixels[i] = PBR.GammaCorrect(accLight / hits);
                    else if (hits > 0)
                        litPixels[i] = (PBR.GammaCorrect(accLight / hits) + (litPixels[i] * state.CurrentFadeIn)) / (state.CurrentFadeIn + 1);
                }
            }

            //public Unity.Profiling.ProfilerMarker RaycastMarker;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void DoRaycast(/*ref LitPoint data,*/ ref int hits, ref float3 accLight, in float3 position, in quaternion pointRot, in float3 normal, in quaternion rayRot, NativeList<(float, LightData)> closestLights)
            {
                var dir = math.mul(math.mul(pointRot, rayRot), math.up());

                var end = position + (dir * Settings.MaxRaycastLength);
                var ray = new RaycastInput
                {
                    Start = position + (normal * 0.01f),
                    End = end,
                    Filter = CollisionFilter.Default,
                };

                //RaycastMarker.Begin();
                //var res = CollisionWorld.CastRay(ray, out var hit);
                //RaycastMarker.End();

                if (CollisionWorld.CastRay(ray, out var hit))
                {
                    hits++;
                    var hitMaterial = RuntimeGIMaterials[hit.Entity];

                    PBR.PrepBounceData(hit.Position, position, hitMaterial.Metallic, hitMaterial.AlbedoF3, out var V, out var F0);

                    var lightCount = math.min(closestLights.Length, LIGHTS_PER_POINT);
                    for (int l = 0; l < lightCount; l++)
                    {
                        accLight += PBR.CalculateLightEffect(hit.Position, hit.SurfaceNormal, hitMaterial.AlbedoF3, position, hitMaterial.Metallic, 1 - hitMaterial.Smoothness, V, F0, closestLights[l].Item2, Settings.UseSphereAreaFalloff, Settings.UsePointLightRadius);
                    }
                }
            }
        }

        [BurstCompile]
        partial struct RaycastLightJob : IJobEntity
        {
            [ReadOnly] public LightData.Comparer LightComparer;

            [ReadOnly] public RuntimeGISettings Settings;
            [ReadOnly] public CollisionWorld CollisionWorld;
            [ReadOnly] public NativeArray<LightData> LightData;

            [ReadOnly] public NativeArray<quaternion> SmallRaySet;
            [ReadOnly] public NativeArray<quaternion> FullRaySet;

            [ReadOnly] public ComponentDataFromEntity<RuntimeGIMaterial> RuntimeGIMaterials;

            void Execute(ref LitPointData data)
            {
                var closestLights = new NativeList<(float, LightData)>(LightData.Length, Allocator.Temp);

                for (int i = 0; i < LightData.Length; i++)
                {
                    var distSqrd = math.distancesq(data.Position, LightData[i].Position);
                    closestLights.Add((distSqrd, LightData[i]));
                }

                closestLights.Sort(LightComparer);

                data.AccLight = default;
                var accLight = (float3)default;
                var hits = 0;

                var normal = data.Normal;
                var accDistSqrd = 0f;

                if (Settings.ExtraProcessCorners)
                {
                    for (int r = 0; r < SmallRaySet.Length; r++)
                    {
                        DoRaycast(ref data, ref hits, ref accDistSqrd, ref accLight, normal, SmallRaySet[r], closestLights);
                    }
                }

                var avgDistSqrd = accDistSqrd / hits;

                if (avgDistSqrd < Settings.CornerThresholdSqrd || Settings.ExtraProcessCorners == false)
                {
                    //accDistSqrd = 0f;
                    for (int r = 0; r < FullRaySet.Length; r++)
                    {
                        DoRaycast(ref data, ref hits, ref accDistSqrd, ref accLight, normal, FullRaySet[r], closestLights);
                    }
                }

                var ambient = new float3(0.03f) * data.Albedo;
                accLight = ambient + accLight;

                data.AccLight += PBR.GammaCorrectOLD(accLight / hits);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void DoRaycast(ref LitPointData data, ref int hits, ref float accDistSqrd, ref float3 accLight, in float3 normal, in quaternion rayRot, NativeList<(float, LightData)> closestLights)
            {
                var dir = math.mul(math.mul(data.Rotation, rayRot), math.up());
                var end = data.Position + (dir * Settings.MaxRaycastLength);
                var ray = new RaycastInput
                {
                    Start = data.Position + (normal * 0.01f),
                    End = end,
                    Filter = CollisionFilter.Default,
                };

                if (CollisionWorld.CastRay(ray, out var hit))
                {
                    hits++;
                    accDistSqrd += math.lengthsq(data.Position - hit.Position);
                    var hitMaterial = RuntimeGIMaterials[hit.Entity];

                    PBR.PrepBounceData(hit.Position, data.Position, hitMaterial.Metallic, hitMaterial.AlbedoF3, out var V, out var F0);

                    var lightCount = math.min(closestLights.Length, LIGHTS_PER_POINT);
                    for (int l = 0; l < lightCount; l++)
                    {
                        accLight += PBR.CalculateLightEffect(hit.Position, hit.SurfaceNormal, hitMaterial.AlbedoF3, data.Position, hitMaterial.Metallic, 1 - hitMaterial.Smoothness, V, F0, closestLights[l].Item2, Settings.UseSphereAreaFalloff, Settings.UsePointLightRadius);
                    }
                }
            }
        }
    }

    // PBR Math
    public static class PBR
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PrepBounceData(float3 worldPos, float3 camPos, float metallic, float3 albedo, out float3 V, out float3 F0)
        {
            V = math.normalize(camPos - worldPos);

            F0 = new float3(0.04f);
            F0 = math.lerp(F0, albedo, metallic);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 GammaCorrectOLD(float3 color)
        {
            color = color / (color + new float3(1.0f));
            color = math.pow(color, new float3(1.0f / 2.2f));

            return new float4(color, 1.0f);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 GammaCorrect(float3 color)
        {
            color = color / (color + new float3(1.0f));
            color = math.pow(color, new float3(1.0f / 2.2f));

            return color;
        }

        // Reference
        static float4 main(float3 worldPos, float3 normal, float3 camPos, float3 albedo, float metallic, float roughness)
        {
            throw new System.NotImplementedException();

            float3 V = math.normalize(camPos - worldPos);

            float3 F0 = new float3(0.04f);
            F0 = math.lerp(F0, albedo, metallic);

            // Dummy for compilation
            var lightPositions = new NativeArray<float3>(4, Allocator.Temp);
            var lightColors = new NativeArray<float3>(4, Allocator.Temp);

            // reflectance equation
            float3 Lo = new float3(0.0);
            for (int i = 0; i < 4; ++i)
            {
                // calculate per-light radiance
                float3 L = math.normalize(lightPositions[i] - worldPos);
                float3 H = math.normalize(V + L);
                float distance = math.length(lightPositions[i] - worldPos);
                float attenuation = 1.0f / (distance * distance);
                float3 radiance = lightColors[i] * attenuation;

                // cook-torrance brdf
                float NDF = DistributionGGX(normal, H, roughness);
                float G = GeometrySmith(normal, V, L, roughness);
                float3 F = fresnelSchlick(math.max(math.dot(H, V), 0.0f), F0);

                float3 kS = F;
                float3 kD = new float3(1.0f) - kS;
                kD *= 1.0f - metallic;

                float3 numerator = NDF * G * F;
                float denominator = 4.0f * math.max(math.dot(normal, V), 0.0f) * math.max(math.dot(normal, L), 0.0f) + 0.0001f;
                float3 specular = numerator / denominator;

                // add to outgoing radiance Lo
                float NdotL = math.max(math.dot(normal, L), 0.0f);
                Lo += (kD * albedo / math.PI + specular) * radiance * NdotL;
            }

            //float3 ambient = new float3(0.03) * albedo * ao;
            float3 color = Lo; //+ ambient

            color = color / (color + new float3(1.0f));
            color = math.pow(color, new float3(1.0f / 2.2f));

            return new float4(color, 1.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 CalculateLightEffect(float3 worldPos, float3 normal, float3 albedo, float3 viewPos, float metallic, float roughness, float3 V, float3 F0, LightData lightData, bool useSphereFalloff, bool usePointLightRadius)
        {
            // calculate per-light radiance
            float3 L = math.normalize(lightData.Position - worldPos);
            float3 H = math.normalize(V + L);

            float attenuation;
            if (usePointLightRadius)
                attenuation = CalculateBouncedPointLightDistanceAttenuation(lightData.Position, worldPos, viewPos, useSphereFalloff);
            else
                attenuation = CalculateBouncedDistanceAttenuation(lightData.Position, worldPos, viewPos, useSphereFalloff);

            float angleMul = GetLightAngleAttenuation(lightData, worldPos);
            float3 radiance = lightData.Color * attenuation * angleMul * lightData.Strength;

            // cook-torrance brdf
            float NDF = DistributionGGX(normal, H, roughness);
            float G = GeometrySmith(normal, V, L, roughness);
            float3 F = fresnelSchlick(math.max(math.dot(H, V), 0.0f), F0);

            float3 kS = F;
            float3 kD = new float3(1.0f) - kS;
            kD *= 1.0f - metallic;

            float3 numerator = NDF * G * F;
            float denominator = 4.0f * math.max(math.dot(normal, V), 0.0f) * math.max(math.dot(normal, L), 0.0f) + 0.0001f;
            float3 specular = numerator / denominator;

            // add to outgoing radiance Lo
            float NdotL = math.max(math.dot(normal, L), 0.0f);
            return (kD * albedo / math.PI + specular) * radiance * NdotL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float DistributionGGX(float3 N, float3 H, float roughness)
        {
            float a = roughness * roughness;
            float a2 = a * a;
            float NdotH = math.max(math.dot(N, H), 0.0f);
            float NdotH2 = NdotH * NdotH;

            float num = a2;
            float denom = (NdotH2 * (a2 - 1.0f) + 1.0f);
            denom = math.PI * denom * denom;

            return num / denom;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float GeometrySchlickGGX(float NdotV, float roughness)
        {
            float r = (roughness + 1.0f);
            float k = (r * r) / 8.0f;

            float num = NdotV;
            float denom = NdotV * (1.0f - k) + k;

            return num / denom;
        }

        static float GeometrySmith(float3 N, float3 V, float3 L, float roughness)
        {
            float NdotV = math.max(math.dot(N, V), 0.0f);
            float NdotL = math.max(math.dot(N, L), 0.0f);
            float ggx2 = GeometrySchlickGGX(NdotV, roughness);
            float ggx1 = GeometrySchlickGGX(NdotL, roughness);

            return ggx1 * ggx2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3 fresnelSchlick(float cosTheta, float3 F0)
        {
            return F0 + (1.0f - F0) * math.pow(math.clamp(1.0f - cosTheta, 0.0f, 1.0f), 5.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateDistanceAttenuation(float3 positionA, float3 positionB, bool useSphereFalloff)
        {
            var distance = math.distance(positionA, positionB);
            if (distance < math.EPSILON)
            {
                return 0; // Too close to make sense
            }

            if (useSphereFalloff)
            {
                var surfaceaArea = 4 * math.PI * math.pow(distance, 2);
                return 1f / surfaceaArea;
            }

            return 1f / (distance * distance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateBouncedDistanceAttenuation(float3 start, float3 end, float3 bounced, bool useSphereFalloff)
        {
            var distance = math.distance(start, bounced) + math.distance(end, bounced);
            if (distance < math.EPSILON)
            {
                return 0; // Too close to make sense
            }

            if (useSphereFalloff)
            {
                var surfaceaArea = 4 * math.PI * math.pow(distance, 2);
                return 1f / surfaceaArea;
            }

            return 1f / (distance * distance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateBouncedPointLightDistanceAttenuation(float3 start, float3 end, float3 bounced, bool useSphereFalloff)
        {
            var distance = math.distance(start, bounced) + math.distance(end, bounced);

            var radius = 0.1f;
            var c1 = 2f / (radius * radius);
            var c2 = 1 - (distance / math.sqrt((distance * distance) + (radius * radius)));

            return c1 * c2;

            if (distance < math.EPSILON)
            {
                return 0; // Too close to make sense
            }

            if (useSphereFalloff)
            {
                var surfaceaArea = 4 * math.PI * math.pow(distance, 2);
                return 1f / surfaceaArea;
            }

            return 1f / (distance * distance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float GetLightAngleAttenuation(LightData lightData, float3 WorldPos)
        {
            var angleMul = 0f;
            var angle = Angle(lightData.Direction, math.normalize(WorldPos - lightData.Position));
            if (angle < lightData.InnerAngle)
                angleMul = 1f;
            else if (angle < lightData.OuterAngle)
                angleMul = 1 - math.clamp((angle - lightData.InnerAngle) / (lightData.OuterAngle - lightData.InnerAngle), 0, 1);

            return angleMul;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Angle(float3 vec1, float3 vec2)
        {
            var res = math.dot(vec1, vec2) / (math.length(vec1) * math.length(vec2));
            return math.acos(math.clamp(res, 0, 1f));
        }
    }
}